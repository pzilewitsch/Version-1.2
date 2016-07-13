using dotNetBF.Core;
using dotNetBF.Messaging;
using dotNetBF.Modules.Services;
using dotNetBF.Modules.Services.Properties;
using dotNetBF.Services;
using GeoMan.GroupWare;
using GeoMan.GroupWare.Exchange;
using Microsoft.Exchange.WebServices.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GeoMan.Common.Core.GroupWare
{
    public abstract class GroupwareMailToObjectFactory : ModuleService
    {
        //Name zu Speicherung der Variablen
        private const string DateFromLastEmailSettingName = "GroupwareMailToObjectFactory_DateFromLastEmail";

        //Log-Eintrag für Diagnosen
        private static readonly dotNetBF.Core.Diagnostics.ILog Log = dotNetBF.Core.Diagnostics.LogManager.GetLogger(typeof(GroupwareMailToObjectFactory));

        //Datum der zuletzt abgerufenen E-Mail im Posteingang des Exchange Servers
        protected virtual DateTime DateFromLastEmail
        {
            get
            {
                //Setting Property holen
                var session = Session.Current;
                var settingsPersistencyService = session.GetService<SettingsPersistencyService>();
			    var settingService = session.GetService<dotNetBF.Services.SettingsService>();
			    DateTime dateTimeValue = ObjectBroker.GetNullValue<DateTime>();
                SystemSettingProperty property = settingsPersistencyService.GetSettingProperty(session, DateFromLastEmailSettingName);
                if (property == null)
                    return dateTimeValue;

                if (!settingService.Settings.Contains(DateFromLastEmailSettingName))
			    {
                    var systemsetting = SystemSetting.QueryExtent(session.Broker, "Property == param1", new QueryParameter("param1", property)).First;
				    if (systemsetting != null)
                    {
                        dateTimeValue = PropertyService.GetValue(systemsetting) as DateTime? ?? dateTimeValue;
                        settingService.Settings.Add(new Setting(DateFromLastEmailSettingName, dateTimeValue));
                    }
			    }
			    else
                    dateTimeValue = settingsPersistencyService.GetValue(session, property) as DateTime? ?? dateTimeValue;
                return dateTimeValue;
            }

            set
            {
                //Setting Property setzen
                var session = Session.Current;
                var settingsPersistencyService = session.GetService<SettingsPersistencyService>();
                var settingService = session.GetService<dotNetBF.Services.SettingsService>();
                DateTime dateTimeValue = value;
                if (!settingService.Settings.Contains(DateFromLastEmailSettingName))
                    settingService.Settings.Add(new Setting(DateFromLastEmailSettingName, null));
                SystemSettingProperty property = settingsPersistencyService.GetOrCreateSettingProperty(session, DateFromLastEmailSettingName);
                property.DataType = dotNetBF.Modules.Services.Properties.PropertyService.DateTimeDataType;
                settingsPersistencyService.SetValue(session, property, dateTimeValue);
            }
        }

        //Einstiegsmethode des ModuleService -> hier wird über das weitere Vorgehen entschieden
        public void MailsToObjects()
        {
            ISessionTransaction session = dotNetBF.Modules.Services.Security.SecurityService.OpenServiceTransaction(Module, TransactionBehavior.NewTransaction);
            FindItemsResults<Item> results;
            results = GetMails(ObjectBroker.GetValueOrDefault(DateFromLastEmail, DateTime.MinValue), session);
            
            if (results.Items.Any())
            {
                DateFromLastEmail = results.Items[0].DateTimeReceived;                  //DateTime der ersten (obersten) Mail merken
                session.Commit();
                try
                {
                    foreach (Item item in results.Items)
                    {
                        using (ISessionTransaction transaction = dotNetBF.Modules.Services.Security.SecurityService.OpenServiceTransaction(Module, TransactionBehavior.NewTransaction))
                        {
                            int id;

                            Regex gotTicketNumber = new Regex(@"#[0-9]");
                            Regex newTicket = new Regex(@"#neu");
                            Regex completedTicket = new Regex(@"#fertig");

                            if (gotTicketNumber.IsMatch(item.Subject))       //wenn der Betreff eine "#Ticketnummer" besitzt 
                            {
                                id = GetObjectId(item);

                                if (completedTicket.IsMatch(item.Subject))               //und "#fertig" besitzt --> schließen
                                    CompleteObject(item, transaction, id);
                                else                                                        //nur #Ticketnummer --> Antwort erstellen                               
                                    ChangeObject(item, transaction, id);
                            }
                            else
                                CreateObject(item, transaction);         //keine Ticketnummer --> Meldung erstellen
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Message);
                }
            }  
        }

        //Methode für das Abrufen der E-Mails vom Exchnage Server
        private FindItemsResults<Item> GetMails(DateTime date, ISessionTransaction session)
        {
            //Server aus Datenbank in Liste speichern
            List<Server> serverList = Server.QueryExtent(session.Broker).ToList();
            var server = serverList.First();

            //ExchangeService erzeugen
            ExchangeService service = new ExchangeService((ExchangeVersion)server.Version);
            service.Credentials = new WebCredentials(server.UserName, Crypto.DecryptString(server.UserPwd), server.Domain);
            service.AutodiscoverUrl(server.UserEmail);

            FindItemsResults<Item> result = null;

            if (ObjectBroker.IsNullValue(date) || date == DateTime.MinValue)
            {
                //maximaler Wert des Integerwertebereiches für die ItemView
                result = service.FindItems(WellKnownFolderName.Inbox, new ItemView(int.MaxValue));

                //einige properties müssen geladen werden, da sonst kein Zugriff
                //auf https://msdn.microsoft.com/en-us/library/bb508824%28EXCHG.80%29.aspx steht, welche das genau sind
                var additionalProperties = new PropertyDefinitionBase[]
                {
                        ItemSchema.DateTimeReceived,
                        ItemSchema.TextBody,
                        ItemSchema.Subject,
                        ItemSchema.Attachments,
                        ItemSchema.MimeContent,
                        ItemSchema.HasAttachments,
                        EmailMessageSchema.From,
                        EmailMessageSchema.ToRecipients,
                        EmailMessageSchema.MimeContent,
                        EmailMessageSchema.Attachments
                };
                service.LoadPropertiesForItems(result, new PropertySet(additionalProperties));
            }
            else
            {
                //Filter für alle mails, die ein "neueres" Datum haben, als die letzte abgefragte Mail
                var filter = new SearchFilter.IsGreaterThan(ItemSchema.DateTimeReceived, date.AddSeconds(1));

                //Die ersten 100 Mails in der Mailbox (Inbox) werden als Item der Item-Collection hinzugefügt
                result = service.FindItems(WellKnownFolderName.Inbox, filter, new ItemView(100));

                if (result.Items.Any())
                {
                    var additionalProperties = new PropertyDefinitionBase[]
                    {
                            ItemSchema.DateTimeReceived,
                            ItemSchema.TextBody,
                            ItemSchema.Subject,
                            ItemSchema.Attachments,
                            ItemSchema.MimeContent,
                            ItemSchema.HasAttachments,
                            EmailMessageSchema.From,
                            EmailMessageSchema.ToRecipients,
                            EmailMessageSchema.MimeContent,
                            EmailMessageSchema.Attachments
                    };
                    service.LoadPropertiesForItems(result, new PropertySet(additionalProperties));
                }
            }
            return result;
        }

        //Methode zum Versenden einer Bestätigungsmail
        public void SendConfirmationMail(Item item, ISessionTransaction session, int id)
        {
            ResponseMessage responseMessage = PrepareReplyMail(item);
            responseMessage.Subject = "#" + id.ToString() + " " + item.Subject;
            responseMessage.BodyPrefix = "Die Meldung mit der ID #" + id.ToString() + " wurde erfolgreich erstellt";
            //responseMessage.SendAndSaveCopy();
        }

        //Methode zum Versenden einer Fehlermail
        public void SendErrorMail(Item item, Exception exception)
        {
            ResponseMessage responseMessage = PrepareReplyMail(item);
            responseMessage.Subject = "Warnung: " + item.Subject;
            responseMessage.BodyPrefix = exception.Message;
            //responseMessage.SendAndSaveCopy();
        }

        //Methode für das Erstellen der Nachricht, die gesendet werden soll
        private ResponseMessage PrepareReplyMail(Item item)
        {
            ExchangeService service = item.Service;     //in den Items sind auch die Informationen des Exchange Service gespeichert
            service.AutodiscoverUrl(((Microsoft.Exchange.WebServices.Data.EmailMessage)item).ToRecipients.First().Address);

            EmailMessage message = EmailMessage.Bind(service, item.Id, BasePropertySet.IdOnly);
            ResponseMessage responseMessage = message.CreateReply(true);
            return responseMessage;
        }

        //abstrakte Methode für das Vervollständigen eines Objektes 
        protected abstract void CompleteObject(Item item, ISessionTransaction session, int id);

        //abstrakte Methode zum Verändern eines Objektes
        protected abstract void ChangeObject(Item item, ISessionTransaction session, int id);

        //abstrakte Methode zum Erstellen eines Objektes
        protected abstract void CreateObject(Item item, ISessionTransaction session);

        //Methode zum identifizieren der ID in der Betreffzeile
        private int GetObjectId(Item item)
        {
            int id;
            int ende = 0;
            int start = item.Subject.IndexOf("#") + 1;
            string cutSubject = item.Subject.Substring(start);

            foreach (char character in cutSubject)
            {
                if (!char.IsNumber(character))
                {
                    ende = cutSubject.IndexOf(character);
                    break;
                }
            }

            if (!int.TryParse(cutSubject.Substring(0, ende), out id))
                throw new Exception(item.Subject + " -> Fehler beim auslesen der ID aus dem Betreff. Ankunft: " + item.DateTimeReceived);

            return id;
        }
    }
}
