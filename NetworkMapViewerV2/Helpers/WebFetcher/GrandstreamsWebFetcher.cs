using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;
using System.Runtime;

namespace NetworkMapViewerV2.Helpers.WebFetcher
{
    public class GrandstreamsWebFetcher
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        public async Task<Dictionary<string, string>> FetchGrandstreamsAsync(string deviceIP)
        {
            return await Task.Run(() =>
            {
                Dictionary<string, string> hintInfo = [];
                string url = $"http://{deviceIP}/";
                string networkStatusUrl = $"{url}#page:status_network";
                string account1Url = $"{url}#page:account_1_general";

                ChromeOptions opt = new();
                opt.AddArgument("--remote-allow-origins=*");
                opt.AddArgument("--ignore-ssl-errors=yes");
                opt.AddArgument("--ignore-certificate-errors");
                opt.AddArgument("--headless=new");
                opt.AddArgument("--window-size=1920,1080");
                opt.AddArgument("--disable-gpu");

                // IMPORTANT FOR BATCH ASYNC: Reduce memory footprint per instance
                opt.AddArgument("--no-sandbox");
                opt.AddArgument("--disable-dev-shm-usage");

                using (ChromeDriver driver = new(opt))
                {
                    try
                    {
                        int maxRetries = 3;
                        bool isPageLoaded = false;

                        // 1. The Retry Loop
                        for (int attempt = 1; attempt <= maxRetries; attempt++)
                        {
                            try
                            {
                                // Use a shorter timeout for the initial load so we can fail fast and retry
                                driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(10);

                                driver.Navigate().GoToUrl(url);

                                // Wait for the login box. If this fails, the page didn't fully render.
                                WaitUntilClickable(driver, By.XPath("//input[contains(@class,'gwt-TextBox')]"));

                                // If we reach this line, the page loaded successfully!
                                isPageLoaded = true;
                                break;
                            }
                            catch (WebDriverTimeoutException)
                            {
                                // If we are on the last attempt, let it fail completely
                                if (attempt == maxRetries)
                                {
                                    hintInfo.Add("ERROR", $"Web interface stuck or unreachable after {maxRetries} attempts.");
                                    return hintInfo;
                                }

                                // Otherwise, the loop continues, and GoToUrl() fires again (which acts as a reload)
                            }
                        }

                        // 2. Safety check: If the loop finished and it never loaded, exit safely.
                        if (!isPageLoaded) return hintInfo;

                        // 1. Set a hard limit on page load times to prevent hanging
                        driver.Manage().Timeouts().PageLoad = DefaultTimeout;

                        driver.Navigate().GoToUrl(url);

                        // Login process
                        SafeWrite(driver, By.XPath("//input[contains(@class,'gwt-TextBox')]"), "admin");
                        SafeWrite(driver, By.XPath("//input[contains(@class, 'gwt-PasswordTextBox')]"), "789Test");
                        WaitUntilClickable(driver, By.XPath("//button[contains(text(),'Login')]")).Click();

                        // Wait for the dashboard to successfully load
                        WaitUntilVisible(driver, By.XPath("//h1[contains(text(),'Account Status')]"));

                        // 2. Wait explicitly for the model banner to populate its text
                        string model = WaitForElementWithText(driver, By.CssSelector("#topBanner .gwt-HTML"), "Grandstream");

                        if (!model.Contains("DP750"))
                        {
                            driver.Navigate().GoToUrl(account1Url);
                            // 3. Replaced Thread.Sleep with an explicit wait for the SIP input field
                            hintInfo.Add("Number", WaitForSipUserId(driver) ?? "");
                        }

                        driver.Navigate().GoToUrl(networkStatusUrl);

                        // 4. Replaced Thread.Sleep. The wait helpers now handle the loading delay.
                        hintInfo.Add("Model", model);
                        hintInfo.Add("MAC", WaitForMacAddress(driver) ?? "");
                        hintInfo.Add("Firmware", WaitForFirmware(driver) ?? "");
                    }
                    catch (WebDriverTimeoutException ex)
                    {
                        hintInfo.Add("ERROR", $"Timeout: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        hintInfo.Add("ERROR", ex.Message);
                    }
                }

                // Add the IP to the result so you know which device this data belongs to
                hintInfo.Add("IP_Address", deviceIP);
                return hintInfo;
            });
        }

        private static IWebElement WaitUntilClickable(ChromeDriver driver, By by)
        {
            var wait = new WebDriverWait(driver, DefaultTimeout);
            return wait.Until(ExpectedConditions.ElementToBeClickable(by));
        }

        private static IWebElement WaitUntilVisible(ChromeDriver driver, By by)
        {
            var wait = new WebDriverWait(driver, DefaultTimeout);
            return wait.Until(ExpectedConditions.ElementIsVisible(by));
        }

        private static void SafeWrite(ChromeDriver driver, By by, string text)
        {
            try
            {
                var element = WaitUntilClickable(driver, by);
                element.Clear();
                element.SendKeys(text);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to write to {by}: {ex.Message}", ex);
            }
        }

        // New helper to safely wait for text to hydrate inside an element
        private static string WaitForElementWithText(ChromeDriver driver, By by, string partialText)
        {
            var wait = new WebDriverWait(driver, DefaultTimeout);
            var element = wait.Until(d =>
            {
                var elements = d.FindElements(by);
                var match = elements.FirstOrDefault(e => e.Text.Contains(partialText));
                return match != null && !string.IsNullOrWhiteSpace(match.Text) ? match : null;
            });
            return element.Text;
        }

        private static string WaitForMacAddress(ChromeDriver driver)
        {
            var wait = new WebDriverWait(driver, DefaultTimeout);
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
                    return null;
                }
            });
            return element.Text.Trim();
        }

        private static string WaitForSipUserId(ChromeDriver driver)
        {
            var wait = new WebDriverWait(driver, DefaultTimeout);
            var element = wait.Until(d =>
            {
                try
                {
                    var el = d.FindElement(By.Name("P35"));
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

        // Extracted firmware wait into its own safe method
        private static string WaitForFirmware(ChromeDriver driver)
        {
            var wait = new WebDriverWait(driver, DefaultTimeout);
            var element = wait.Until(d =>
            {
                try
                {
                    var el = d.FindElement(By.CssSelector("#verNo .gwt-HTML"));
                    return string.IsNullOrWhiteSpace(el.Text) ? null : el;
                }
                catch (NoSuchElementException)
                {
                    return null;
                }
            });
            return element.Text.Replace("Version ", "").Trim();
        }
    }
}