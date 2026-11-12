# Published browser runtime

Use a matching [published output](published-output.md). Reuse an external Playwright installation through `NODE_PATH`; otherwise install it and the needed browser outside tracked source. Select an installed Chromium executable with `YGO_BROWSER_EXECUTABLE`, or a channel with `YGO_BROWSER_CHANNEL`. Do not assume Windows Edge exists on a cloud host.
