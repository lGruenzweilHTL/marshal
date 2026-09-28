---
name: house-status
description: Gives a short status report of the house (open doors and windows, lights left on, locks, climate). Use when the user asks how the house is or before leaving or going to bed.
---

# House status

1. Call `ha_homeassistant__GetLiveContext` once to read the current states of the exposed entities.
2. Report only what needs attention, most important first:
   - unlocked locks and open garage doors, doors or windows
   - lights and media players that are on in empty rooms
   - temperatures far from the usual range
3. If everything is fine, say so in one sentence.
4. Never change anything while reporting. Offer an action instead ("Shall I lock the front door?").

Keep voice answers to two or three sentences; in text you may use a short list.
