# SessionStart hook — parallel-work reminder.
# Fires only when the session runs in the MAIN working tree (not a linked worktree).
# stdout of a SessionStart hook is injected into Claude's context, so this note is
# addressed to Nyxie (the assistant), who relays it to Max when relevant.
#
# Detection: in the main worktree `git-dir` == `git-common-dir`; in a linked
# worktree they differ.

$ErrorActionPreference = 'SilentlyContinue'

$gitDir    = (git rev-parse --git-dir 2>$null)
$commonDir = (git rev-parse --git-common-dir 2>$null)

# Not a git repo — nothing to say.
if (-not $gitDir) { exit 0 }

# Linked worktree — Max is already isolated, stay quiet.
if ($gitDir -ne $commonDir) { exit 0 }

# Main working tree:
Write-Output @'
[parallel-work reminder] This session runs in the MAIN working tree, not a git worktree.
If substantial code work is coming AND other sessions may be active: remind Max to spin up / switch to a worktree before editing, and run list_sessions at the start of the task to see who is doing what. If this is a lone session, docs-only, or a small fix, ignore this note.
'@
