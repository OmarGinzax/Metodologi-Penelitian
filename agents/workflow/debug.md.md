---
description: Debug a bug using hypotheses, runtime logs, and user-verified fixes
---

Do NOT write a fix yet. Follow these steps in order:

1. Restate the bug and what I expect vs. what happens. Ask me for
   missing repro steps or error text.
2. Read the relevant code. List 3-5 distinct hypotheses for the cause,
   ranked by likelihood.
3. Add temporary logging (prefix every line with [DBG]) that will
   confirm or rule out each hypothesis. Change no behavior.
4. Tell me exactly how to reproduce the bug, then stop and wait for
   me to paste the logs (or run the app and read the output yourself
   if you can).
5. Name which hypothesis the evidence supports and which it rules out.
   If none fit, add more logging and go back to step 4.
6. Apply the smallest fix for the confirmed cause.
7. Ask me to re-test. Only after I confirm it's fixed, remove every
   [DBG] line and summarize the root cause.