---
name: no-claude-coauthor-trailer
description: Do NOT add the Co-Authored-By Claude trailer to git commits in this repo
metadata: 
  node_type: memory
  type: feedback
  originSessionId: b576ee5b-15f8-4176-a79f-dea8b3999e3a
---

For the AudioHQ repo, commit messages must NOT include a `Co-Authored-By: Claude ...` trailer. The user wants only "underfusion" listed under GitHub Contributors.

**Why:** The trailer made "Claude" appear in the repo's Contributors list; the user explicitly asked to remove it everywhere and keep only themselves.

**How to apply:** Omit any `Co-Authored-By: Claude ...` line when committing here, even though the default harness convention adds one. On 2026-06-16 the existing history was rewritten (filter-branch) to strip the trailer from all commits and tags, then force-pushed to main, dev, and the v0.2.6/v0.3.4 tags.
