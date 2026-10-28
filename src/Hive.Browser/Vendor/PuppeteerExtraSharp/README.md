# Stealth source compatibility subset

Source: https://github.com/Overmiind/Puppeteer-sharp-extra/tree/e3ec018da1a921cdd657ca399c217c34a8b329a2/PuppeteerExtraSharp
Upstream commit: `e3ec018da1a921cdd657ca399c217c34a8b329a2`. License: MIT (preserved in LICENSE).

Only StealthPlugin, its evasion scripts, plugin contracts and embedded-resource reader are included. Captcha vendors and unrelated plugins are excluded.

The published PuppeteerExtraSharp 3.1.1 binary targets PuppeteerSharp 20.2.2 and fails with MissingMethodException for IBrowser.PagesAsync on PuppeteerSharp 25.15.0. This subset is compiled against Hive's current PuppeteerSharp. Hive owns the launch/page lifecycle and awaits each plugin before navigating; no upstream fire-and-forget event registration is used. Browser cleanup stays in Hive.

Changes: nullable context retained as legacy-disabled; current browser major version replaces hardcoded 139 client-hint brands; private-reflection SourceUrl evasion is disabled; resources retain original logical names. Newly created managed pages receive Stealth before navigation. User-opened popup tabs are outside this managed-page injection path.
