# GitHub writing

Lead with the concrete problem and resulting or desired behavior. Scale detail to complexity; explain contracts, tradeoffs and useful before/after examples without recounting the conversation. When scope changes, rewrite the title/body around the final scope. Link the relevant issue or prerequisite and preserve genuinely undecided requirements.

Omit routine verification inventories from issues, PR descriptions and comments, including expected local tests, CI and privacy checks. Include unreconciled verification blockers and non-routine evidence useful to reviewers or future agents, such as measured benchmarks. Keep routine results in the task conversation; do not add a report file.

Use structured body arguments, or an exact UTF-8 body file with `gh --body-file`; preserve real newlines. Before publishing text or screenshots, apply the Public content section of `repo:scripts/codex/PRIVACY.md`; reading that section does not require installing local hooks.
