# Syncing DFMP with Upstream Daggerfall Unity

This document describes how to pull changes from upstream `Interkarma/daggerfall-unity`
into DFMP. Follow it exactly; the ordering exists to keep the fork rebasable and to
avoid force-pushing a branch that other contributors have cloned.

## Policy: merge, never rebase

`master` is published. Upstream is merged into it, never rebased onto it.

Rebasing rewrites every SHA, requires `git push --force`, and breaks every clone, open
PR, and commit reference. It also replays conflicts once per commit instead of once
total. A merge resolves each conflict a single time and pushes normally.

Never rebase or force-push `master`.

There is deliberately no separate branch for the Layer 1 hook edits. The complete hook
patch against upstream is derivable at any time:

```sh
git diff upstream/master..master -- Assets/Scripts
```

That is strictly better than a long-lived branch: it is always accurate, costs nothing
to maintain, and cannot drift. See the Layer 1 audit in step 9.

## One-time setup per clone

```sh
git remote add upstream https://github.com/Interkarma/daggerfall-unity.git

# Replay previously recorded conflict resolutions automatically.
git config rerere.enabled true

# Collapse line-ending-only differences instead of conflicting on them.
git config merge.renormalize true
```

Both settings are local-only and cannot be committed, which is why they are listed
here and in `.github/copilot-instructions.md`.

### Unity Smart Merge (recommended)

Scene, prefab, and asset conflicts are not resolvable by hand. Unity ships
`UnityYAMLMerge` for this. Register it as a merge driver in your global Git config,
pointing at the tool inside your Unity install (`Editor/Data/Tools/UnityYAMLMerge.exe`
on Windows), then let `.gitattributes` route the relevant file types to it.

Without this, a conflicted `.unity` or `.prefab` file usually has to be discarded and
re-authored.

## Step 1 - Start clean

```sh
git switch master
git status --short --untracked-files=all   # must be empty
```

Do not begin a sync with uncommitted work. Commit or park it on a branch first.

## Step 2 - Fetch and measure

```sh
git fetch upstream --tags

git rev-list --count HEAD..upstream/master   # commits coming in
git rev-list --count upstream/master..HEAD   # our commits on top
```

A stale `upstream/master` ref will report zero incoming commits, so always fetch
before judging the size of the sync.

## Step 3 - Pre-flight checks

Do these **before** merging. Any one of them can turn a routine sync into its own
milestone.

**3a. Unity editor version.** This is the highest-risk item.

```sh
git diff HEAD...upstream/master -- ProjectSettings/ProjectVersion.txt
```

If upstream has moved to a newer Unity LTS, stop. Upgrading the editor affects every
contributor and every build target, and must be planned as its own piece of work
rather than discovered mid-merge.

**3b. What upstream changed that we also changed.** This set is your conflict list.

```sh
# Note the three dots: changes since the merge base, not a raw comparison.
git diff --name-only HEAD...upstream/master
```

Cross-reference the result against the hook sites registered in `HOOKS.md` and
against `Assets/DFMP/Runtime/ThirdParty/`.

**3c. Read the incoming history.**

```sh
git log --oneline --no-merges HEAD..upstream/master
```

Watch in particular for changes to `GameManager`, `StreamingWorld`, `SceneControl`,
`PlayerEnterExit`, and `StartGameBehaviour`. The DFMP headless bootstrap and world
transition paths depend on their behavior.

## Step 4 - Back up

```sh
git tag pre-upstream-sync-YYYY-MM-DD
```

Cheap, local, and gives you an unambiguous point to return to.

## Step 5 - Merge on a disposable branch

Never merge directly into `master`. If the merge turns out to be a bad idea, you want
to be able to walk away by deleting a branch rather than unwinding `master`.

```sh
git switch -c integrate/upstream-YYYY-MM-DD
git merge --no-ff upstream/master
```

`--no-ff` guarantees a single, clearly labelled integration commit even in the rare
case where a fast-forward is possible.

## Step 6 - Resolve conflicts

```sh
git status                 # list conflicted paths
git log --merge -p -- <path>   # show the commits from both sides for one file
```

Guidance by conflict type:

- **Hook sites in `Assets/Scripts/**`** - Re-apply the hook against upstream's new
  code rather than restoring our old block verbatim. The hook must stay additive and
  under ~5 lines. If upstream restructured the method so the hook no longer fits,
  that is a signal the hook needs redesigning, not forcing. Update `HOOKS.md` if the
  method or location changed.
- **`Assets/DFMP/**`** - Should never conflict. If it does, someone violated the
  layer boundary; investigate rather than resolving mechanically.
- **`Assets/DFMP/Runtime/ThirdParty/`** - Vendored Mirror and kcp2k. Upstream DFU
  does not ship these, so conflicts here mean a path collision worth understanding.
- **`ProjectSettings/`, `Packages/manifest.json`** - Take upstream's changes unless
  the setting is one DFMP deliberately owns. Note which ones we own as you go.
- **Scenes, prefabs, assets** - Use UnityYAMLMerge. Do not hand-edit the YAML.
- **Line endings** - Should not occur; `merge.renormalize` handles them. If you see a
  whole-file conflict where every line changed, that is a line-ending problem, not a
  content problem. See the Line Endings section of `.github/copilot-instructions.md`.

Then:

```sh
git add <resolved paths>
git commit          # completes the merge
```

To abandon at any point before committing: `git merge --abort`.

## Step 7 - Verify before landing

A merge that compiles is not a merge that works. Unity must reimport the changed
assets first; let the editor finish and settle.

1. **Compilation** - Zero errors in the Unity console.
2. **Line endings** - `git ls-files --eol` reports only `i/lf w/lf`, `i/-text w/-text`,
   and `i/none w/none`. If upstream introduced CRLF files, run `git add --renormalize .`
   and commit; never hand-edit.
3. **EditMode tests** - Run the DFMP fixtures in `Assets/DFMP/Tests/Editor` via the
   Unity Test Runner. Do not use Unity batchmode; the editor holds the project lock.
4. **Headless smoke test** - Boot the dedicated server and confirm it reaches its
   ready state without a live player.
5. **Client join smoke test** - Connect a client, confirm spawn, movement, chat, and
   at least one world transition.

Steps 4 and 5 are mandatory whenever upstream touched the boot, scene, or transition
paths identified in step 3c.

## Step 8 - Land it

```sh
git switch master
git merge --ff-only integrate/upstream-YYYY-MM-DD
git push origin master          # a plain push; never --force
git branch -d integrate/upstream-YYYY-MM-DD
```

If `--ff-only` fails, `master` moved while you were working. Merge the new `master`
into your integration branch, re-verify, and try again.

## Step 9 - Re-audit Layer 1

An upstream sync is the moment the hook layer is most likely to have grown. Measure it:

```sh
# Complete Layer 1 footprint.
git diff --stat --ignore-cr-at-eol upstream/master..master -- Assets/Scripts

# Must print nothing. Layer 1 may never depend on a networking library.
git grep -nE "using Mirror|NetworkBehaviour|NetworkServer|NetworkClient" -- Assets/Scripts
```

Compare the result against the baseline recorded in `HOOKS.md`. Growth that is not
explained by a newly registered hook means multiplayer logic has leaked into Layer 1;
move it back into `Assets/DFMP/` rather than absorbing it.

If upstream restructured a method so a hook no longer fits cleanly, update the entry in
`HOOKS.md` to match its new location.

## Cadence and version targeting

Sync regularly rather than in large batches; small merges are proportionally easier
to reason about and to verify.

Consider tracking upstream releases or tags instead of `master`. For a project other
people build servers against, a known-good DFU release is a calmer base than upstream
tip.

## If it goes wrong

- Mid-merge, uncommitted: `git merge --abort`
- Merge committed but only on the integration branch: delete the branch.
- Already on `master`: `git revert -m 1 <merge-commit>` creates a new commit undoing
  the merge. Do not reset or force-push a published branch.
- Total loss of bearings: the tag from step 4 is still there.
