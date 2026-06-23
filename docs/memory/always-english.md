---
name: always-english
description: "User wants all communication in English, including chat replies"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: d7abd227-92e6-4abc-8d54-92c0c7849135
---

Always write in English - code, docs, commits AND conversational replies to the user. Even though the user often writes to me in Polish, my responses must be in English.

**Why:** The user explicitly asked (2026-06-11) to always reply in English, overriding the CLAUDE.md default that allowed Polish replies when requested per message.

**How to apply:** Default every response to English regardless of the language the user writes in, unless they explicitly ask for Polish in a specific message.
