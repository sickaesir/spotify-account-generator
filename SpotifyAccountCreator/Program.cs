using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Console = Colorful.Console;

namespace SpotifyAccountCreator
{
    class Program
    {

        static object ioLocker = new object();
        private static Random random = new Random();
        static string RandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFFGHIJKLMNOPQRSTUVWXYZ1234567890";
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        static async Task<string> SolveCaptcha(string solverApiKey)
        {
            lock(ioLocker) Console.WriteLine($"[2Captcha] Solving captcha challenge", Color.Magenta);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create($"https://2captcha.com/in.php?key={solverApiKey}&method=userrecaptcha&googlekey=6LdaGwcTAAAAAJfb0xQdr3FqU4ZzfAc_QZvIPby5&pageurl=https://www.spotify.com/us/signup/&json=1");

            string requestId = "";
            using (var respsonse = await request.GetResponseAsync())
            using (var responseReader = new StreamReader(respsonse.GetResponseStream()))
            {
                string responseText = await responseReader.ReadToEndAsync();

                dynamic responseData = JsonConvert.DeserializeObject<dynamic>(responseText);

                if(responseData.status != 1)
                {
                    lock (ioLocker) Console.WriteLine($"[Error] Failed to solve captcha, 2Captcha error: {(int)responseData.status}", Color.Red);
                    return null;
                }

                requestId = (string)responseData.request;
            }

            lock (ioLocker) Console.WriteLine($"[2Captcha] Captcha solve request successful, captcha id: {requestId}", Color.Magenta);


            lock (ioLocker) Console.WriteLine($"[2Captcha] Retrieving {requestId} solved captcha", Color.Magenta);
            while (true)
            {
                HttpWebRequest captchaSolutionRequest = (HttpWebRequest)WebRequest.Create($"https://2captcha.com/res.php?key={solverApiKey}&action=get&id={requestId}&json=1");


                using (var respsonse = await captchaSolutionRequest.GetResponseAsync())
                using (var responseReader = new StreamReader(respsonse.GetResponseStream()))
                {
                    string responseText = await responseReader.ReadToEndAsync();

                    dynamic responseData = JsonConvert.DeserializeObject<dynamic>(responseText);

                    if(responseData.status == 0)
                    {
                        await Task.Delay(5000);
                        continue;
                    }
                    else if(responseData.status == 1)
                    {
                        lock (ioLocker) Console.WriteLine($"[2Captcha] Captcha {requestId} solved!", Color.Green);

                        return (string)responseData.request;
                    }
                    else
                    {
                        lock (ioLocker) Console.WriteLine($"[Error] Failed to solve captcha, 2Captcha error: {(int)responseData.status}", Color.Red);
                        return null;
                    }

                }
            }
        }

        static async Task<KeyValuePair<string, string>> CreateSpotifyAccount(string captchaSolverApiKey)
        {
            string randomEmail = $"{RandomString(10)}@gmail.com";
            string randomPassword = RandomString(10);
            string displayName = RandomString(5);

            lock (ioLocker) Console.WriteLine($"[Spotify] Starting {randomEmail} account creation", Color.DodgerBlue);

            CookieContainer container = new CookieContainer();


            string solvedCaptcha = await SolveCaptcha(captchaSolverApiKey);

            lock (ioLocker) Console.WriteLine($"[Spotify] Obtaining Spotify CSRF token", Color.DodgerBlue);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create($"https://www.spotify.com/us/signup/");

            string csrfToken = "";

            using(var response = (HttpWebResponse)await request.GetResponseAsync())
            using(var responseReader = new StreamReader(response.GetResponseStream()))
            {
                string responseText = await responseReader.ReadToEndAsync();

                Regex csrfRegex = new Regex(@"signup_form\[sp_csrf\].+?value=.(.+?).\s\/>");
                csrfToken = csrfRegex.Match(responseText).Groups[1].Value;

                container.Add(response.Cookies);
            }


            if (solvedCaptcha == null) return new KeyValuePair<string, string>();

            lock (ioLocker) Console.WriteLine($"[Spotify] Creating account", Color.DodgerBlue);
            HttpWebRequest createAccountRequest = (HttpWebRequest)WebRequest.Create($"https://www.spotify.com/us/xhr/json/sign-up-for-spotify.php");
            createAccountRequest.CookieContainer = container;
            createAccountRequest.ContentType = "application/x-www-form-urlencoded; charset=UTF-8";
            createAccountRequest.Accept = "application/json, text/javascript, */*; q=0.01";
            createAccountRequest.Method = "POST";
            createAccountRequest.Referer = "https://www.spotify.com/us/signup/";
            createAccountRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");
            createAccountRequest.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:73.0) Gecko/20100101 Firefox/73.0";

            using (var requestStream = await createAccountRequest.GetRequestStreamAsync())
            {
                await new FormUrlEncodedContent(new Dictionary<string, string>()
                {
                    { "signup_form[data_source]", "www" },
                    { "signup_form[sp_csrf]", csrfToken },
                    { "signup_form[creation_flow]", "" },
                    { "signup_form[forward_url_parameter]", "" },
                    { "signup_form[signup_pre_tick_eula]", "true" },
                    { "signup_form[email]", randomEmail },
                    { "signup_form[confirm_email]", randomEmail },
                    { "signup_form[password]", randomPassword },
                    { "signup_form[displayname]", displayName },
                    { "signup_form[dob_month]", "01" },
                    { "signup_form[dob_day]", "12" },
                    { "signup_form[dob_year]", "2000" },
                    { "signup_form[gender]", "neutral" },
                    { "g-recaptcha-response", solvedCaptcha },
                    { "captcha_hidden", "" }
                }).CopyToAsync(requestStream);
            }

            using(var response = await createAccountRequest.GetResponseAsync())
            using(var responseReader = new StreamReader(response.GetResponseStream()))
            {
                string responseText = await responseReader.ReadToEndAsync();

                if (responseText.Contains("Created"))
                {
                    lock (ioLocker) Console.WriteLine($"[Spotify] Account {randomEmail} created!", Color.Green);
                    return new KeyValuePair<string, string>(randomEmail, randomPassword);
                }

                lock (ioLocker) Console.WriteLine($"[Error] Failed to create {randomEmail} account, error: {responseText}", Color.Red);

            }

            return new KeyValuePair<string, string>();

        }

        static void Main(string[] args)
        {
            Console.WriteLine($"Spotify Account Creator - by Aesir - [Discord: Aesir#1337] [Nulled: SickAesir] [Telegram: @sickaesir]", Color.Aqua);

            int accounts2Create = 0;
            string captchaSolverKey = "";


            while(true)
            {
                Console.Write("[Config] Number of accounts to create: ", Color.Orange);
                string accounts2CreateString = Console.ReadLine();

                if(!int.TryParse(accounts2CreateString, out accounts2Create))
                {
                    Console.WriteLine("[Error] Please input a valid number!", Color.Red);
                    continue;
                }
                break;
            }

            Console.Write($"[Config] 2captcha API key: ", Color.Orange);
            captchaSolverKey = Console.ReadLine();


            string filename = $"SpotifyAccounts_{DateTime.Now.Year}_{DateTime.Now.Month}_{DateTime.Now.Day}_{DateTime.Now.Hour}_{DateTime.Now.Minute}_{DateTime.Now.Second}.txt";

            string filePath = Path.Combine(Environment.CurrentDirectory, filename);


            List<Thread> threads = new List<Thread>();

            for(int i = 0; i < 20; i++)
            {
                Thread thread = new Thread(() =>
                {
                    while(accounts2Create > 0)
                    {
                        Interlocked.Decrement(ref accounts2Create);
                        try
                        {
                            var accountInfo = CreateSpotifyAccount(captchaSolverKey).Result;

                            if (accountInfo.Key == null || accountInfo.Value == null)
                            {
                                Interlocked.Increment(ref accounts2Create);
                                continue;
                            }


                            lock (ioLocker)
                            {
                                File.AppendAllText(filePath, $"{accountInfo.Key}:{accountInfo.Value}\n");

                                Console.Title = $"Accounts left to create: {accounts2Create}";
                            }
                        }
                        catch(Exception)
                        {
                            lock (ioLocker) Console.WriteLine($"[Error] Exception occurred, probably rate limited, waiting 5 seconds", Color.Red);
                            Thread.Sleep(5000);
                            Interlocked.Increment(ref accounts2Create);
                            continue;
                        }


                    }
                });

                Thread.Sleep(500);
                thread.Start();

                threads.Add(thread);
            }

            foreach (var thread in threads)
                thread.Join();

        }
    }
}
