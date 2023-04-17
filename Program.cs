using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Collections;

namespace JoinUsers
{
    class Program
    {
        public static HttpClient Hclient = new HttpClient(); //один общий клиент чтобы избежать переполнения HTTP сокетов

        static async Task Main(string[] args)
        {
            var config = Config.GetConfig();

            var authenticationBytes = Encoding.UTF8.GetBytes($"{config.AdminName}:{config.AdminPass}");
            Hclient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));           
            Hclient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authenticationBytes));

            string ServerAdress = config.ServerName; //Console.ReadLine();
            string allUsersUri = "http://" + ServerAdress + $":{config.ServerPort}/api/v1/users";
            string SingleuserURI = "http://" + ServerAdress + $":{config.ServerPort}/api/v1/users/";
            string SingGroup = "http://" + ServerAdress + $":{config.ServerPort}/api/v1/groups/";
            Log("=========================== Start ======================");
            Log($" \n ServerSettings\n \n ServerNmae:{config.ServerName} \n ServerPort: {config.ServerPort}\n UserNmae: {config.AdminName} \n UserPass{config.AdminPass}");

            string allUsers = await GetUsers(allUsersUri);
            Hashtable AzureUsersForDelete = new Hashtable();

            if (allUsers == null)
            {
                Log("Cant connect to the Server");
                Console.WriteLine("Cant connect to the Server");

            }
            else
            {
                allUsers = allUsers.Replace("$", "");//в получаемом JSON есть $id и $value с символом $ не получается десерилизовать 
                UserArray users = JsonSerializer.Deserialize<UserArray>(allUsers);
                var SortedUssers = from p in users.values orderby p.FirstName descending select p;
                List<User> AllUsers = new List<User>();
                foreach (var user in SortedUssers)
                {
                    AllUsers.Add(user); //формируем отсортированный лист с пользователями 
                }
                
                for (int i = 0; i < AllUsers.Count -1; i++) // берем первого пользователя в отсортированном списке 
                {
                    for (int j = i; j < AllUsers.Count - 1; j++) //сравниваем со всеми кто дальше по списку 
                    {
                        if (AllUsers[i].FirstName == AllUsers[j].FirstName + " " + AllUsers[j].LastName && AllUsers[i].SIDs.values.Length >= 1) //сравниваем если firstnmae совпадает с firstname + Lastname и берем только тех у кого не нудевой SID
                        {
                            
                            var AzureUser = await GetSingleUsers(SingleuserURI + AllUsers[i].ID.ToString());
                            var DomainUser = await GetSingleUsers(SingleuserURI + AllUsers[j].ID.ToString());
                            var jsonAzureUser = JsonSerializer.Deserialize<SinglUser>(AzureUser);
                            var jsonDomainUser = JsonSerializer.Deserialize<SinglUser>(DomainUser);
                            var AzureMembership = await GetSingleUsers(SingleuserURI + jsonAzureUser.ID.ToString() + "/membership"); //получаем массив групп в которые входит пользователь
                            Membership[] AzureUserMembership = JsonSerializer.Deserialize<Membership[]>(AzureMembership); // десереиализуем группы 
                            Console.WriteLine($"AzureUser {jsonAzureUser.ID} {jsonAzureUser.FirstName} >>>> {jsonDomainUser.ID} {jsonDomainUser.FirstName} {jsonDomainUser.LastName}");
                            Log($"      =================   Start processing Users  ========================");
                            Log($"AzureUser:{jsonAzureUser.ID} {jsonAzureUser.FirstName}");
                            Log($"DomainUser{jsonDomainUser.ID} {jsonDomainUser.FirstName} {jsonDomainUser.LastName}");
                            foreach (var group in AzureUserMembership) //добаление пользователя AD в группу где состоит пользователь Azure
                            {
                                bool isAdUserPresent = false;
                                foreach (var user in group.Users) //проходим по всем пользователям в каждой группе 
                                {
                                    if (user.ID == jsonDomainUser.ID) //ставиф флаг если AD пользователь уже состоит в группе 
                                    {
                                        isAdUserPresent = true;
                                    }

                                }
                                if (!isAdUserPresent)// если AD пользователя нет в группе 
                                {
                                    Log($"AzureUser:{jsonAzureUser.ID} {jsonAzureUser.FirstName} {string.Join(",", jsonAzureUser.SIDs)} Member OF: {group.Name}  {group.ID}");
                                    Log($"DomainUser {jsonDomainUser.ID} {jsonDomainUser.FirstName}  {jsonDomainUser.LastName} Add To Group {group.Name} ");
                                    List<SinglUser> SingleUserList = group.Users.Cast<SinglUser>().ToList(); //перегоняем в список чтобы проще было обавить
                                    SingleUserList.Add(jsonDomainUser); // добавляем нашего доменного пользователя
                                    group.Users = SingleUserList.ToArray();
                                    string groupForPUT = JsonSerializer.Serialize<Membership>(group);
                                    var tt = await PUTtSingleUsers(SingGroup + group.ID.ToString(), groupForPUT); //добавляем пользователя AD в группу где состоит одноименный пользователь Azure
                                }

                            }
                            //обьединяем массивы Union по умолчанию не делает дублей
                            jsonDomainUser.YahooAccounts = jsonDomainUser.YahooAccounts.Union(jsonAzureUser.YahooAccounts).ToArray();
                            Log($"YahooAccounts { string.Join(",", jsonAzureUser.YahooAccounts)} >>>{ string.Join(",", jsonDomainUser.YahooAccounts)}");
                            jsonDomainUser.TelegramAccounts = jsonDomainUser.TelegramAccounts.Union(jsonAzureUser.TelegramAccounts).ToArray();
                            Log($"TelegramAccounts { string.Join(",", jsonAzureUser.TelegramAccounts)} >>>{ string.Join(",", jsonDomainUser.TelegramAccounts)}");

                            jsonDomainUser.SocialNetworkIDs = jsonDomainUser.SocialNetworkIDs.Union(jsonAzureUser.SocialNetworkIDs).ToArray();
                            Log($"SocialNetworkIDs { string.Join(",", jsonAzureUser.SocialNetworkIDs)} >>>{ string.Join(",", jsonDomainUser.SocialNetworkIDs)}");

                            jsonDomainUser.SkypeAccounts = jsonDomainUser.SkypeAccounts.Union(jsonAzureUser.SkypeAccounts).ToArray();
                            Log($"SkypeAccounts { string.Join(",", jsonAzureUser.SkypeAccounts)} >>>{ string.Join(",", jsonDomainUser.SkypeAccounts)}");

                            jsonDomainUser.SIDs = jsonDomainUser.SIDs.Union(jsonAzureUser.SIDs).ToArray();
                            Log($"SIDs { string.Join(",", jsonAzureUser.SIDs)} >>>{ string.Join(",", jsonDomainUser.SIDs)}");

                            jsonDomainUser.Phones = jsonDomainUser.Phones.Union(jsonAzureUser.Phones).ToArray();
                            Log($"Phones { string.Join(",", jsonAzureUser.Phones)} >>>{ string.Join(",", jsonDomainUser.Phones)}");

                            jsonDomainUser.LocalUserDNs = jsonDomainUser.LocalUserDNs.Union(jsonAzureUser.LocalUserDNs).ToArray();
                            Log($"LocalUserDNs { string.Join(",", jsonAzureUser.LocalUserDNs)} >>>{ string.Join(",", jsonDomainUser.LocalUserDNs)}");

                            jsonDomainUser.ICQUINs = jsonDomainUser.ICQUINs.Union(jsonAzureUser.ICQUINs).ToArray();
                            Log($"ICQUINs { string.Join(",", jsonAzureUser.ICQUINs)} >>>{ string.Join(",", jsonDomainUser.ICQUINs)}");

                            jsonDomainUser.EMails = jsonDomainUser.EMails.Union(jsonAzureUser.EMails).ToArray();
                            Log($"EMails { string.Join(",", jsonAzureUser.EMails)} >>>{ string.Join(",", jsonDomainUser.EMails)}");

                            //присваиваем Azureuser пустые значения 
                            string[] emptyString = new string[] { };
                            jsonAzureUser.YahooAccounts = emptyString;
                            jsonAzureUser.TelegramAccounts = emptyString;
                            jsonAzureUser.SocialNetworkIDs = emptyString;
                            jsonAzureUser.SkypeAccounts = emptyString;
                            jsonAzureUser.SIDs = emptyString;
                            jsonAzureUser.Phones = emptyString;
                            jsonAzureUser.LocalUserDNs = emptyString;
                            jsonAzureUser.ICQUINs = emptyString;
                            jsonAzureUser.EMails = emptyString;


                            string PutAzutenUser = JsonSerializer.Serialize<SinglUser>(jsonAzureUser);
                            var test2 = await PUTtSingleUsers(SingleuserURI + jsonAzureUser.ID.ToString(), PutAzutenUser);
                            Log($"PutChanges for {jsonAzureUser.FirstName} - {test2}");
                            string PutDomainUser = JsonSerializer.Serialize<SinglUser>(jsonDomainUser);

                            var test = await PUTtSingleUsers(SingleuserURI + jsonDomainUser.ID.ToString(), PutDomainUser);
                           Log($"PutChanges for {jsonDomainUser.FirstName} {jsonDomainUser.LastName} - {test}");
                            //Log($"      =================   END processing Users  ========================");
                           
                            Console.WriteLine();
                            if (!AzureUsersForDelete.ContainsKey(jsonAzureUser.ID))
                            {
                                AzureUsersForDelete.Add(jsonAzureUser.ID, jsonAzureUser.FirstName);
                            }
                           
                        }
                    }
                }
                Console.WriteLine(AzureUsersForDelete);
                Console.WriteLine("Start to deleting Users");
                Log("===================== Users For Deleting ===================== ");
                foreach (var item in AzureUsersForDelete.Keys)
                {
                    Console.WriteLine($"{item.ToString()} { AzureUsersForDelete[item]}");
                    Log($"{item.ToString()} { AzureUsersForDelete[item]}");
                   
                }
                string keys = string.Join(",", AzureUsersForDelete.Keys.Cast<object>()
                                         .Select(x => x.ToString())
                                         .ToArray());
                keys = "[" + keys + "]";
               // keys = "[9190,9191]";
               var isdeleted = await DeleteUser(allUsersUri, keys);
                Console.WriteLine($"DeletedStatus {isdeleted}");
                Log($"DeletedStatus {isdeleted}");
            }


            Console.WriteLine("Done, press any key");
        }

        public static async Task<string> DeleteUser(string DeleteUuserURI, string userIDArray)
        {
            HttpRequestMessage request = new HttpRequestMessage();

            request.Content = new StringContent(userIDArray, Encoding.UTF8, "application/json");
                request.Method = HttpMethod.Delete;
            request.RequestUri = new Uri(DeleteUuserURI);              
             var result = await Hclient.SendAsync(request);
            return result.StatusCode.ToString();
            //await httpClient.SendAsync(request);

        }

        public static async Task<string> PUTtSingleUsers(string SingleuserURI, string json)
        {      
            
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var result = await Hclient.PutAsync(SingleuserURI, content);
                var result_string = await result.Content.ReadAsStringAsync();
            Log("PUTtSingleUsers" + SingleuserURI);
            Log(result.StatusCode.ToString());
            return result_string;
            
        }

        public static async Task<string> GetSingleUsers(string SingleuserURI)
        {
            var result = await Hclient.GetAsync(SingleuserURI);
            var result_string = await result.Content.ReadAsStringAsync();
            Log("GetRequst" + SingleuserURI);
           // Log(SingleuserURI);
            Log(result.StatusCode.ToString());
            return result_string;            
        }

        public static async Task<string> GetUsers(string URI)
        {
            try
            {
                var result = await Hclient.GetAsync(URI);
                Log(result.StatusCode.ToString());
                if (!result.IsSuccessStatusCode)
                {
                    Log(result.ReasonPhrase);
                    Console.WriteLine(result.ReasonPhrase);
                    return null;
                }
                var result_string = await result.Content.ReadAsStringAsync();
                Log("GetAllUsers");
                Log(result.StatusCode.ToString());
                return result_string;
            }
            catch (Exception ex)
            {
                Log(ex.GetType().FullName);
                return null;
            } 
            
        }


        public  class Config : SeverConfig
        {

            static string configFileName = Path.Join(Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName), "Config.json");

            public static SeverConfig GetConfig()            {
                const string SettinsFile = "C:\\ProgramData\\Falcongaze SecureTower\\UsersAuthServer\\FgStAuthServerSettings.yaml";
                if (File.Exists(SettinsFile)) //если есть файл настроек будем брать настройки из него
                {
                    string[] Srverconf = File.ReadAllLines(SettinsFile);
                    SeverConfig sever = new SeverConfig();
                    sever.AdminName = (Srverconf[13].Split(':'))[1].Trim().TrimEnd('"');
                    sever.AdminPass = (Srverconf[14].Split(':'))[1].Trim().TrimEnd('"');
                    sever.ServerName = "localhost";
                    sever.ServerPort = (Srverconf[6].Split(':'))[1].Trim().TrimEnd('"');
                    Console.WriteLine($"Server config have beed readed");
                    Console.WriteLine($"ServerName: {sever.ServerName}");
                    Console.WriteLine($"ServerPort: {sever.ServerPort}");
                    Console.WriteLine($"AdminName: {sever.AdminName }");
                    Console.WriteLine($"AdminPass: {sever.AdminPass }");
                    return sever;
                }
                else
                {
                    string conf = @"{
                                ""ServerName"": ""localhost"",
                                ""ServerPort"": ""39001"",
                                ""AdminName"": ""admin"",
                                ""AdminPass"": ""123""
                                }";




                    if (!File.Exists(configFileName))
                    {
                        SeverConfig? severConfig = JsonSerializer.Deserialize<SeverConfig>(conf);
                        string temp = JsonSerializer.Serialize<SeverConfig>(severConfig);
                        File.WriteAllText(configFileName, temp);

                        Console.WriteLine($"Set the Server connection parameters to {configFileName}");
                        Console.WriteLine("Save the configuration file and press any key");
                        Console.ReadLine();

                    }

                    SeverConfig? TT = JsonSerializer.Deserialize<SeverConfig>(File.ReadAllText(configFileName));
                    return TT;
                }

            }
            

        }

        public class SeverConfig
        {
            [JsonPropertyName("ServerName")]
            public  string ServerName { get; set; }
            [JsonPropertyName("ServerPort")]
            public  string ServerPort { get; set; }
            [JsonPropertyName("AdminName")]
            public  string AdminName { get; set; }
            [JsonPropertyName("AdminPass")]
            public  string AdminPass { get; set; }
        }

        public static void Log(string text)
        {
            DateTime now = DateTime.Now;
            string logFileName = now.ToString("dd.MM.yyyy");
            string FileLog = "LOG_" + logFileName + ".txt";

            using (StreamWriter sw = File.AppendText(FileLog))
            {
                sw.WriteLine($"{now:G}: {text}");
            }
        }

        public static void Data(List<string> dataName, string DataFileName)
        {
            DateTime now = DateTime.Now;
            string lofFileName = $"{now.ToString("dd.MM.yyyy")}_{DataFileName}.txt";
   
            using (StreamWriter sw = File.AppendText(lofFileName))
            {
                foreach (var item in dataName)
                {
                    sw.WriteLine($"{item}");
                }
              
            }
        }

    }

}
