using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace DFMP.Runtime
{
    public interface IDFMPDiscordAuthClient
    {
        IEnumerator ExchangeCode(string code, string codeVerifier, string redirectUri, Action<string, string> completed);
        IEnumerator GetUserId(string accessToken, Action<string, string> completed);
        IEnumerator GetGuildMember(string userId, string guildId, Action<DFMPDiscordGuildMember, string> completed);
    }

    /// <summary>
    /// Discord REST client used only on the dedicated server. Tokens stay here; the game client
    /// never receives them.
    /// </summary>
    public sealed class DFMPDiscordHttpClient : IDFMPDiscordAuthClient
    {
        const string ApiRoot = "https://discord.com/api/v10";
        const string UserAgent = "DFMP-Server (https://github.com, 1.0)";

        readonly string clientId;
        readonly string clientSecret;
        readonly string botToken;

        public DFMPDiscordHttpClient(string clientId, string clientSecret, string botToken)
        {
            // Values arrive from the environment, where a stray quote or trailing newline is easy to
            // introduce and produces an opaque "invalid client id" from Discord.
            this.clientId = DFMPDiscordSecrets.Clean(clientId);
            this.clientSecret = DFMPDiscordSecrets.Clean(clientSecret);
            this.botToken = DFMPDiscordSecrets.Clean(botToken);
        }

        /// <summary>
        /// Redeems the authorization code the player's browser handed back. The client secret makes
        /// this a confidential exchange, so an intercepted code cannot be redeemed by anyone else.
        /// </summary>
        public IEnumerator ExchangeCode(string code, string codeVerifier, string redirectUri, Action<string, string> completed)
        {
            WWWForm form = new WWWForm();
            form.AddField("grant_type", "authorization_code");
            form.AddField("code", code ?? string.Empty);
            form.AddField("redirect_uri", redirectUri ?? string.Empty);
            form.AddField("code_verifier", codeVerifier ?? string.Empty);
            form.AddField("client_id", clientId);
            form.AddField("client_secret", clientSecret);

            UnityWebRequest request = UnityWebRequest.Post(ApiRoot + "/oauth2/token", form);
            request.SetRequestHeader("User-Agent", UserAgent);

            yield return request.SendWebRequest();

            string body = ReadBody(request);
            DiscordTokenJson parsed = string.IsNullOrEmpty(body) ? null : JsonUtility.FromJson<DiscordTokenJson>(body);

            if (parsed != null && !string.IsNullOrEmpty(parsed.access_token))
            {
                completed(parsed.access_token, string.Empty);
                yield break;
            }

            completed(string.Empty, DescribeFailure("code exchange", request, body));
        }

        public IEnumerator GetUserId(string accessToken, Action<string, string> completed)
        {
            UnityWebRequest request = UnityWebRequest.Get(ApiRoot + "/users/@me");
            request.SetRequestHeader("Authorization", "Bearer " + accessToken);
            request.SetRequestHeader("User-Agent", UserAgent);

            yield return request.SendWebRequest();

            if (HasRequestError(request) || request.responseCode < 200 || request.responseCode >= 300)
            {
                completed(string.Empty, DescribeFailure("user lookup", request, ReadBody(request)));
                yield break;
            }

            DiscordUserJson parsed = JsonUtility.FromJson<DiscordUserJson>(ReadBody(request));
            if (parsed == null || string.IsNullOrEmpty(parsed.id))
            {
                completed(string.Empty, "discord user lookup returned an unreadable payload");
                yield break;
            }

            completed(parsed.id, string.Empty);
        }

        public IEnumerator GetGuildMember(string userId, string guildId, Action<DFMPDiscordGuildMember, string> completed)
        {
            if (string.IsNullOrEmpty(botToken))
            {
                completed(null, "discord bot token is unavailable");
                yield break;
            }

            string url = ApiRoot + "/guilds/" + UnityWebRequest.EscapeURL(guildId) + "/members/" + UnityWebRequest.EscapeURL(userId);
            UnityWebRequest request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Authorization", "Bot " + botToken);
            request.SetRequestHeader("User-Agent", UserAgent);

            yield return request.SendWebRequest();

            if (request.responseCode == 404)
            {
                completed(new DFMPDiscordGuildMember { IsMember = false, RoleIds = new string[0] }, string.Empty);
                yield break;
            }

            if (HasRequestError(request) || request.responseCode < 200 || request.responseCode >= 300)
            {
                completed(null, DescribeFailure("guild member lookup", request, ReadBody(request)));
                yield break;
            }

            DiscordGuildMemberJson parsed = JsonUtility.FromJson<DiscordGuildMemberJson>(ReadBody(request));
            completed(
                new DFMPDiscordGuildMember
                {
                    IsMember = true,
                    RoleIds = parsed != null && parsed.roles != null ? parsed.roles : new string[0]
                },
                string.Empty);
        }

        // Discord's own error payload is the only useful diagnostic here, and it never contains the
        // secret, so it is safe to log and to show the rejected player.
        static string DescribeFailure(string stage, UnityWebRequest request, string body)
        {
            DiscordErrorJson parsed = null;
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    parsed = JsonUtility.FromJson<DiscordErrorJson>(body);
                }
                catch (Exception)
                {
                    parsed = null;
                }
            }

            string detail = parsed != null && !string.IsNullOrEmpty(parsed.error)
                ? parsed.error
                : (string.IsNullOrEmpty(body) ? "no response body" : body);

            string summary = $"discord {stage} failed (HTTP {request.responseCode}: {detail})";
            Debug.LogError($"[DFMP Auth] {summary}. Full body: '{body}'.");
            return summary;
        }

        static bool HasRequestError(UnityWebRequest request)
        {
#if UNITY_2020_1_OR_NEWER
            return request.result != UnityWebRequest.Result.Success &&
                request.result != UnityWebRequest.Result.ProtocolError;
#else
            return request.isNetworkError;
#endif
        }

        static string ReadBody(UnityWebRequest request)
        {
            return request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        }

        [Serializable]
        class DiscordTokenJson
        {
            public string access_token;
            public string error;
        }

        [Serializable]
        class DiscordErrorJson
        {
            public string error;
            public string error_description;
        }

        [Serializable]
        class DiscordUserJson
        {
            public string id;
        }

        [Serializable]
        class DiscordGuildMemberJson
        {
            public string[] roles;
        }
    }
}
