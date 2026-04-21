using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;

namespace NetworkMapViewerV2.Helpers.WebFetcher
{
    internal class GrandstreamsWebFetcher
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        public Dictionary<string, string> FetchGrandstreams(string deviceIP)
        {
            Dictionary<string, string> HintInfo = [];
            string url = $"http://{deviceIP}/";
            string NetworkStatus = $"{url}#page:status_network";
            string Accaunt1 = $"{url}#page:account_1_general";

            ChromeOptions opt = new();
            opt.AddArgument("--remote-allow-origins=*");
            opt.AddArgument("--ignore-ssl-errors=yes");
            opt.AddArgument("--ignore-certificate-errors");

            // --- THE FIX: RUN CHROME INVISIBLY ---
            opt.AddArgument("--headless=new");
            opt.AddArgument("--window-size=1920,1080"); // Required for headless to render elements
            opt.AddArgument("--disable-gpu");

            using (ChromeDriver driver = new(opt))
            {
                try
                {
                    driver.Navigate().GoToUrl(url);
                    WaitUntilClickable(driver, By.XPath("//input[contains(@class,'gwt-TextBox')]"));

                    //login process
                    SafeWrite(driver, By.XPath("//input[contains(@class,'gwt-TextBox')]"), "admin");
                    SafeWrite(driver, By.XPath("//input[contains(@class, 'gwt-PasswordTextBox')]"), "789Test");
                    WaitUntilClickable(driver, By.XPath("//button[contains(text(),'Login')]")).Click();

                    WaitUntilClickable(driver, By.XPath("//h1[contains(text(),'Account Status')]"));


                    string model = driver.FindElements(By.CssSelector("#topBanner .gwt-HTML")).First(e => e.Text.Contains("Grandstream")).Text;

                    if (!model.Contains("DP750"))
                    {
                        driver.Navigate().GoToUrl(Accaunt1);
                        Thread.Sleep(500);
                        HintInfo.Add("Number", WaitForSipUserId(driver) ?? "");
                    }

                    driver.Navigate().GoToUrl(NetworkStatus);

                    Thread.Sleep(900); // Wait for the network status page to load
                    HintInfo.Add("Model", model);
                    HintInfo.Add("MAC", WaitForMacAddress(driver));
                    HintInfo.Add("Firmware", driver.FindElement(By.CssSelector("#verNo .gwt-HTML")).Text.Replace("Version ", ""));
                }
                catch (Exception ex)
                {
                    HintInfo.Add("ERROR", ex.Message);
                }
            }
            return HintInfo;
        }

        private static IWebElement WaitUntilClickable(ChromeDriver driver, By by)
        {
            var wait = new WebDriverWait(driver, DefaultTimeout);
            return wait.Until(ExpectedConditions.ElementToBeClickable(by));
        }

        private static void SafeWrite(ChromeDriver driver, By by, string text)
        {
            try
            {
                var element = WaitUntilClickable(driver, by);
                element.Clear();
                element.SendKeys(text);
            }
            catch (Exception ex) { throw new Exception($"Failed to write to {by}: {ex.Message}"); }
        }

        private static string WaitForMacAddress(ChromeDriver driver)
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

            var element = wait.Until(d =>
            {
                try
                {
                    var el = d.FindElement(By.XPath(
                        "//div[contains(@class,'label') and normalize-space()='MAC Address']" +
                        "/following::div[contains(@class,'gwt-HTML')][1]"
                    ));

                    return string.IsNullOrWhiteSpace(el.Text) ? null : el;
                }
                catch (NoSuchElementException)
                {
                    return null; // keep waiting instead of crashing
                }
            });

            return element.Text.Trim();
        }

        private static string WaitForSipUserId(ChromeDriver driver)
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

            var element = wait.Until(d =>
            {
                try
                {
                    var el = d.FindElement(By.Name("P35")); // stable selector

                    var value = el.GetAttribute("value");
                    return string.IsNullOrWhiteSpace(value) ? null : el;
                }
                catch (NoSuchElementException)
                {
                    return null;
                }
            });

            return element.GetAttribute("value").Trim();
        }
    }
}
