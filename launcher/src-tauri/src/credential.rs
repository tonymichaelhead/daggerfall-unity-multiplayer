use pbkdf2::pbkdf2_hmac;
use sha2::Sha256;

/// Must stay in lockstep with DFMPCredential in the Unity client.
const CLIENT_ITERATIONS: u32 = 600_000;
const SALT_PREFIX: &str = "dfmp-credential-v1:";
const KEY_LENGTH: usize = 32;

const MIN_PASSWORD_LENGTH: usize = 8;
const MAX_PASSWORD_LENGTH: usize = 256;
const MAX_ACCOUNT_ID_LENGTH: usize = 128;

pub fn normalize_account_id(input: &str) -> Result<String, String> {
    let normalized = input.trim().to_lowercase();

    if normalized.is_empty() {
        return Err("Enter a username.".into());
    }

    if normalized.len() > MAX_ACCOUNT_ID_LENGTH {
        return Err("That username is too long.".into());
    }

    if !normalized
        .chars()
        .all(|c| c.is_ascii_alphanumeric() || matches!(c, ':' | '.' | '_' | '-'))
    {
        return Err("Usernames may only contain letters, numbers, and the characters : . _ -".into());
    }

    Ok(normalized)
}

pub fn check_password(password: &str) -> Result<(), String> {
    if password.chars().count() < MIN_PASSWORD_LENGTH {
        return Err(format!("Your password must be at least {MIN_PASSWORD_LENGTH} characters."));
    }

    if password.chars().count() > MAX_PASSWORD_LENGTH {
        return Err(format!("Your password must be at most {MAX_PASSWORD_LENGTH} characters."));
    }

    Ok(())
}

/// PBKDF2-HMAC-SHA256 over the password, salted with the account id so the result is reproducible
/// on any machine. The server never receives the password itself.
pub fn derive_client_credential(account_id: &str, password: &str) -> String {
    let salt = format!("{SALT_PREFIX}{account_id}");
    let mut out = [0u8; KEY_LENGTH];
    pbkdf2_hmac::<Sha256>(password.as_bytes(), salt.as_bytes(), CLIENT_ITERATIONS, &mut out);
    hex::encode(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn known_answer_matches_rfc_vector() {
        // PBKDF2-HMAC-SHA256, P="password", S="salt", dkLen=32. Mirrors the C# fixture exactly.
        let mut out = [0u8; KEY_LENGTH];
        pbkdf2_hmac::<Sha256>(b"password", b"salt", 1, &mut out);
        assert_eq!(
            hex::encode(out),
            "120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b"
        );

        pbkdf2_hmac::<Sha256>(b"password", b"salt", 2, &mut out);
        assert_eq!(
            hex::encode(out),
            "ae4d0c95af6b46d32d0adff928f06dd02a303f8ef3c251dfd6e2d85a95474c43"
        );
    }

    #[test]
    fn derivation_is_account_scoped() {
        assert_ne!(
            derive_client_credential("one", "shared-password"),
            derive_client_credential("two", "shared-password")
        );
    }

    #[test]
    fn account_ids_are_normalized_and_validated() {
        assert_eq!(normalize_account_id("  Tony "), Ok("tony".into()));
        assert!(normalize_account_id("bad name").is_err());
        assert!(normalize_account_id("").is_err());
    }
}
