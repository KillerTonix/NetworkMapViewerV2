using NetworkMapViewerV2.Helpers.Passwords;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;

namespace NetworkMapViewerV2.Helpers.WebFetcher
{
    internal class PrintersWebFetcher
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
        private static AppSettings settings = SettingsService.Load();
        private static string decryptedPassword = SecureSettingsHelper.UnprotectPassword(settings.PrinterPassword) ?? "";
        public Dictionary<string, string> FetchPrinters(string deviceIP)
        {
            Dictionary<string, string> HintInfo = [];
            string url = $"https://{deviceIP}/"; // Note: Many newer HPs force HTTPS!
            string httpUrl = $"http://{deviceIP}/"; // Fallback
            string networkUrl = $"{url}#hId-pgNetworkSummary";
            string networkUrl426 = $"{url}info_config_network.html?tab=Networking&menu=NetConfig";

            ChromeOptions opt = new();
            opt.AddArgument("--remote-allow-origins=*");
            opt.AddArgument("--ignore-ssl-errors=yes");
            opt.AddArgument("--ignore-certificate-errors");

            // --- THE FIX: RUN CHROME INVISIBLY ---
            opt.AddArgument("--headless=new");
            opt.AddArgument("--window-size=1920,1080"); // Required for headless to render elements
            opt.AddArgument("--disable-gpu");

            // --- THE FIX: 'using' BLOCK ENSURES CHROME CLOSES EVEN ON ERROR ---
            using (ChromeDriver driver = new(opt))
            {
                try
                {
                    // Try HTTPS first, fallback to HTTP if it fails
                    try { driver.Navigate().GoToUrl(url); }
                    catch { driver.Navigate().GoToUrl(httpUrl); }

                    WaitUntilClickable(driver, By.XPath("//a[contains(text(), 'Home')]"));

                    bool OldInterface = driver.FindElements(By.XPath("//h1[contains(text(),'MFP M426')]")).Count > 0
                                     || driver.FindElements(By.XPath("//h1[contains(text(),'M402dn')]")).Count > 0
                                     || driver.FindElements(By.XPath("//h1[contains(text(),'MFP M283')]")).Count > 0;

                    bool NewInterface = driver.FindElements(By.XPath("//h1[contains(text(),'MFP M428')]")).Count > 0;

                    if (OldInterface)
                    {
                        driver.Navigate().GoToUrl(networkUrl426);
                        WaitUntilClickable(driver, By.XPath("//h1[contains(text(),'Network Summary')]"));

                        // --- THE FIX: ADDED '?.' TO PREVENT NULL CRASHES ---
                        var hostLabel = driver.FindElements(By.XPath("//td[@class='labelFont' and normalize-space(text())='Host Name:']")).FirstOrDefault();
                        string? hostValueCell = hostLabel?.FindElements(By.XPath("following-sibling::td[@class='itemFont']")).FirstOrDefault()?.Text.Trim();
                        HintInfo.Add("HostName", hostValueCell ?? "Unknown");

                        var macLabel = driver.FindElements(By.XPath("//td[@class='labelFont' and normalize-space(text())='Hardware Address:']")).FirstOrDefault();
                        string? macValueCell = macLabel?.FindElements(By.XPath("following-sibling::td[@class='itemFont']")).FirstOrDefault()?.Text.Trim().ToUpperInvariant();
                        HintInfo.Add("MAC", macValueCell ?? "Unknown");

                        string? modelLabel = driver.FindElements(By.XPath("//h1[contains(text(),'HP LaserJet')]")).FirstOrDefault()?.Text.Trim();
                        HintInfo.Add("Model", modelLabel ?? "HP LaserJet");
                    }
                    else if (NewInterface)
                    {
                        driver.Navigate().GoToUrl(networkUrl);
                        ProcessNewInterface(driver, HintInfo);
                    }
                    else // 4103 model
                    {
                        driver.Navigate().GoToUrl(networkUrl);
                        //WaitUntilClickable(driver, By.XPath("//button[contains(text(),'OK')]")).Click();
                        WaitUntilClickable(driver, By.XPath("//input[@type='password']"));

                        var passBox = driver.FindElement(By.XPath("//input[@type='password']"));
                        passBox.Clear();
                        passBox.SendKeys(decryptedPassword);

                        var submitBtn = driver.FindElement(By.XPath("//button[contains(text(),'Submit')]"));
                        ClickJS(driver, submitBtn);

                        ProcessNewInterface(driver, HintInfo);
                    }
                }
                catch (Exception ex)
                {
                    // Instead of a MessageBox (which halts background threads), put the error in the dictionary!
                    HintInfo.Add("ERROR", ex.Message);
                }
                // When this block ends, the 'using' statement automatically calls driver.Quit() and kills the processes!
            }
            return HintInfo;
        }

        private void ProcessNewInterface(ChromeDriver driver, Dictionary<string, string> HintInfo)
        {
            WaitUntilClickable(driver, By.XPath("//span[normalize-space()='IP Address']"));

            string HostName = driver.FindElements(By.CssSelector("#nS-wD-HostName")).FirstOrDefault()?.Text.Trim() ?? "Unknown";
            string RawMac = driver.FindElements(By.CssSelector("#nS-wD-MacAddr")).FirstOrDefault()?.Text.Trim() ?? "";

            string Mac = "Unknown";
            if (RawMac.Length == 12)
            {
                Mac = string.Join(":", Enumerable.Range(0, 6).Select(i => RawMac.Substring(i * 2, 2).ToUpperInvariant()));
            }

            string? modelLabel = driver.FindElements(By.XPath("//h1[contains(text(),'HP LaserJet')]")).FirstOrDefault()?.Text.Trim();

            HintInfo.Add("HostName", HostName);
            HintInfo.Add("MAC", Mac);
            HintInfo.Add("Model", modelLabel ?? "HP LaserJet");
        }

        private static IWebElement WaitUntilClickable(ChromeDriver driver, By by)
        {
            var wait = new WebDriverWait(driver, DefaultTimeout);
            return wait.Until(ExpectedConditions.ElementToBeClickable(by));
        }

        private static void ClickJS(IWebDriver driver, IWebElement el)
        {
            var js = (IJavaScriptExecutor)driver;
            js.ExecuteScript("arguments[0].click();", el);
        }
    }
}