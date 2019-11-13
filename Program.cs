using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Vendorlink.QueryToFile
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(new String('*', 80));
            Console.WriteLine("*");
            Console.WriteLine("* Welcom to Vendorlink Query To File");
            Console.WriteLine("*");
            Console.WriteLine("* This console application will allow you to export any of your Vendorlink queries to a file.");
            Console.WriteLine("* Please follow the instructions step by step to get your results.");
            Console.WriteLine("*");
            Console.WriteLine(new String('*', 80));
            Console.WriteLine();
            Console.WriteLine("Please enter the hostname for your Vendorlink application. If you login to Vendorlink and check the URL in your browser, the hostname is the first part after \"https://\". For example: \"app.vendorlink.nl\".");
            var hostname = Console.ReadLine();
            Console.WriteLine("Now please enter your username. Please note that this application does not store your username or password.");
            var username = Console.ReadLine();
            Console.WriteLine("Now please enter your password. Your password will not be displayed.");
            var password = "";

            var key = Console.ReadKey(true);
            while (key.Key != ConsoleKey.Enter)
            {
                password += key.KeyChar.ToString();
                key = Console.ReadKey(true);
            }

            Console.WriteLine("");
            Console.WriteLine("We'll try to login to your account now.");

            var creds = new Credentials();
            while (string.IsNullOrEmpty(creds.AccessToken))
            {
                creds = Login(hostname, username, password);
            }

            Console.WriteLine($"Hello {creds.DisplayName}.");
            Console.WriteLine("Please wait while we have look at what queries you have available in Vendorlink.");

            var queries = GetQueries(hostname, creds);

            Console.WriteLine($"We found {queries.Count} queries. Please choose the one you need:");
            string number = "";
            Query selectedQuery = null;
            while (selectedQuery == null)
            {
                foreach (var query in queries)
                {
                    Console.WriteLine($"{queries.IndexOf(query) + 1:0}: {query.Name}");
                }
                number = Console.ReadLine();
                int i = -1;
                if (int.TryParse(number, out i) && i > 0 && i <= queries.Count)
                {
                    selectedQuery = queries[i - 1];
                }

                if (selectedQuery == null)
                {
                    Console.WriteLine("Sorry, can't use that. Please type the number of one of the queries:'");
                }
            }

            Console.WriteLine($"Alright. So, you need the data from {selectedQuery.Name}.");
            Console.WriteLine("Please wait while we download the results for your query.");

            var queryResultToken = GetJsonFromRequest(hostname, creds, $"/API/Queries/Execute?id={selectedQuery.Id}", "GET", "");
            // we need the value of the first property: that contains the array
            var resultsArray = (JArray)queryResultToken[selectedQuery.Name];

            Console.WriteLine($"Done. There seem to be {resultsArray.Count} results.");

            ToCsv(selectedQuery, resultsArray);

            Console.WriteLine("We're done. Press any key to close");
            Console.ReadKey();
        }

        internal static void ToCsv(Query selectedQuery, JArray results)
        {
            var filename = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{selectedQuery.Name}.csv");
            Console.WriteLine($"Writing the results to {filename}");

            var sb = new StringBuilder();
            var propNames = new List<string>();
            foreach (JProperty property in ((JObject)results[0]).Properties())
            {
                propNames.Add(property.Name);
                sb.Append($"{property.Name};");
            }

            foreach (JObject jObject in results)
            {
                bool firstProp = true;
                foreach (JProperty property in jObject.Properties())
                {
                    if (!firstProp) sb.Append(";");
                    var propVal = property.Value.ToString();
                    propVal = propVal
                        .Replace("\r\n", " ")
                        .Replace("  ", " ")
                        .Replace("\"", "\"\"");
                    sb.Append($"\"{propVal}\"");
                    firstProp = false;
                }

                sb.AppendLine();
            }

            File.WriteAllText(filename, sb.ToString());
        }
        //internal static void ToXml(Query selectedQuery, JArray results)
        //{
        //    var filename = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{selectedQuery.Name}.xml");
        //    Console.WriteLine($"Writing the results to {filename}");

        //    var sb = new StringBuilder();
        //    var propNames = new List<string>();
        //    foreach (JProperty property in ((JObject)results[0]).Properties())
        //    {
        //        propNames.Add(property.Name.Replace(" ", ""));
        //        //sb.Append($"{property.Name};");
        //    }

        //    sb.Append("<root>");
        //    foreach (JObject jObject in results)
        //    {
        //        bool firstProp = true;
        //        foreach (JProperty property in jObject.Properties())
        //        {
        //            if (!firstProp) sb.Append(";");
        //            var propVal = property.Value.ToString();
        //            propVal = propVal
        //                .Replace("\r\n", " ")
        //                .Replace("  ", " ")
        //                .Replace("\"", "\"\"");
        //            sb.Append($"<{propNames[]}\"{propVal}\"");
        //            firstProp = false;
        //        }

        //        sb.AppendLine();
        //    }
        //    sb.Append("</root>");

        //    File.WriteAllText(filename, sb.ToString());
        //}

        internal static List<Query> GetQueries(string hostname, Credentials creds)
        {
            var retval = new List<Query>();

            var jToken = GetJsonFromRequest(hostname, creds, "/API/Queries/List", "GET", "");
            var jArray = (JArray)jToken;

            foreach (JToken jObject in jArray)
            {
                retval.Add(new Query()
                {
                    Id = jObject["Id"].Value<string>(),
                    Name = jObject["Name"].Value<string>()
                });
            }

            return retval;
        }

        private static JToken GetJsonFromRequest(string hostname, Credentials creds, string requestPath, string httpMethod, string body)
        {
            try
            {
                var webReq = WebRequest.Create($"https://{hostname}{requestPath}");
                webReq.Method = httpMethod;
                webReq.ContentType = "application/json";
                webReq.Headers.Add("Authorization", $"Bearer {creds.AccessToken}");

                if (!String.IsNullOrEmpty(body))
                {
                    ASCIIEncoding encoding = new ASCIIEncoding();
                    byte[] byte1 = encoding.GetBytes(body);
                    // Set the content length of the string being posted.
                    webReq.ContentLength = byte1.Length;
                    // get the request stream
                    Stream newStream = webReq.GetRequestStream();
                    // write the content to the stream
                    newStream.Write(byte1, 0, byte1.Length);
                }

                var webResp = (HttpWebResponse)webReq.GetResponse();

                if (webResp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Console.WriteLine("Login expired. Please start over.");
                    return null;
                }

                var readStream = new StreamReader(webResp.GetResponseStream(), Encoding.UTF8);
                var jToken = JToken.Parse(readStream.ReadToEnd());

                return jToken;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        internal static Credentials Login(string hostname, string username, string password)
        {
            var retval = new Credentials();

            var webReq = WebRequest.Create($"https://{hostname}/API/auth/login");
            webReq.Method = "POST";
            webReq.ContentType = "application/json; charset=utf-8";
            string postData = $"{{\"u\":\"{username}\",\"p\":\"{password}\"}}";
            ASCIIEncoding encoding = new ASCIIEncoding();
            byte[] byte1 = encoding.GetBytes(postData);
            // Set the content length of the string being posted.
            webReq.ContentLength = byte1.Length;
            // get the request stream
            Stream newStream = webReq.GetRequestStream();
            // write the content to the stream
            newStream.Write(byte1, 0, byte1.Length);
            // execute the request
            var webResp = webReq.GetResponse();

            if (webResp.ContentLength <= 0)
            {
                Console.WriteLine("Login failed");
                return retval;
            }

            var readStream = new StreamReader(webResp.GetResponseStream(), Encoding.UTF8);
            var jToken = (JObject)JToken.Parse(readStream.ReadToEnd());
            if (jToken["displayName"] != null)
            {
                retval.DisplayName = jToken["displayName"].Value<string>();
                retval.UserId = jToken["userId"].Value<int>();
                retval.AccessToken = jToken["accessToken"].Value<string>();
                retval.RenewalToken = jToken["renewalToken"].Value<string>();
            }

            return retval;
        }

        internal class Credentials
        {
            internal string DisplayName { get; set; }
            internal int UserId { get; set; }
            internal string AccessToken { get; set; }
            internal string RenewalToken { get; set; }

        }

        internal class Query
        {
            internal string Id { get; set; }
            internal string Name { get; set; }
        }
    }

}
