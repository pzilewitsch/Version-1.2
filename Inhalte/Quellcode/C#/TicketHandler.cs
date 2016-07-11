using Bmm.Services;
using dotNetBF.Modules.Services.Documents;
using dotNetBF.Modules.Services.Security;
using dotNetBF.Security;
using dotNetBF.Services;
using GeoMan.Common.Core;
using GeoMan.Common.Core.GroupWare;
using Microsoft.Exchange.WebServices.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GeoMan.ServiceDesk
{
    public class TicketHandler : GroupwareMailToObjectFactory
    {
        protected override void CompleteObject(Item item, ISessionTransaction session, int id)
        {
            try
            {
                Ticket ticket = GetTicket(session, id);
                if (item.TextBody.Text.IsNotNullOrWhiteSpace())
                {
                    Answer answer = Answer.Create(session.Broker);
                    answer.Author = ticket.ResponsiblePerson ?? null;
                    answer.Ticket = ticket;
                    answer.Message = item.TextBody.Text;
                    answer.Done = true;
                    if (item.HasAttachments)
                        SaveAttachments(item, session, ticket);
                }
            }
            catch (Exception exception)
            {
                //SendErrorMail(item, exception);
            }
        }

        protected override void ChangeObject(Item item, ISessionTransaction session, int id)
        {
            try
            {
                Ticket ticket = GetTicket(session, id);
                if (item.TextBody.Text.IsNotNullOrWhiteSpace())
                {
                    Answer answer = Answer.Create(session.Broker);
                    answer.Author = ticket.ResponsiblePerson ?? null;
                    answer.Ticket = ticket;
                    answer.Message = item.TextBody.Text;
                    
                    if (item.HasAttachments)
                        SaveAttachments(item, session, ticket);

                    session.Commit();
                    int nr = ticket.OID;
                }
                else
                    throw new Exception("Der MailBody war leer");
            }
            catch (Exception exception)
            {
                if (exception is InvalidOperationException)
                    exception = new Exception("Kein Ticket mit der Ticketnummer #" + id.ToString() + " vorhanden.");

                //SendErrorMail(item, exception);
            }
        }

        protected override void CreateObject(Item item, ISessionTransaction session)
        {
            try
            {
                var ticket = Ticket.Create(session.Broker);
                ticket.PersonAssignments.Where(x => x.Person == null || x.Type == null).ForEach(session.Broker.Delete);
                ticket.Message = item.Subject;
                ticket.Description = item.TextBody.Text;
                ticket.Status = KnownObjects.Untouched;
                if (item.IsAttachment)
                    SaveAttachments(item, session, ticket);
 
                session.Commit();
                //SendConfirmationMail(item, session, ticket.OID);     //Bestätigungsmail senden
            }
            catch (Exception exception)
            {
                exception = new Exception("Es ist ein Fehler beim Erstellen der Meldung aufgetreten.");
                //SendErrorMail(item, exception);
            }
        }

        protected void SaveAttachments(Item item, ISessionTransaction session, Ticket ticket)
        {        
            try
            {
                foreach (Attachment attachment in item.Attachments)
                {
                    //Dokument erstellen
                    Document doc = Document.Create(session.Broker);
                    doc.CreateDate = DateTime.Now;
                    doc.Type = KnownObjects.ServiceDeskDocumentDefaultType;
                    doc.Ticket = ticket;
                    doc.Extension = Path.GetExtension(attachment.Name);
                    doc.Name = Path.GetFileNameWithoutExtension(attachment.Name);

                    //Anhnag der Mail in das neu erstellte Dokument einfügen
                    if (attachment is FileAttachment)
                    {
                        FileAttachment fileAttachment = attachment as FileAttachment;
                        fileAttachment.Load();
                        Bmm.Services.DocumentService.SetDocumentContent(doc, fileAttachment.Content);
                    }
                    else if (attachment is ItemAttachment)
                    {
                        ItemAttachment itemAttachment = attachment as ItemAttachment;
                        itemAttachment.Load(ItemSchema.MimeContent);
                        Bmm.Services.DocumentService.SetDocumentContent(doc, itemAttachment.Item.MimeContent.Content);
                    }
                }
            }
            catch (Exception exception)
            {
                exception = new Exception("Es ist ein Fehler beim Speichern des Anhangs aufgetreten.");
                //SendErrorMail(item, exception);
            }
        }

        /// <summary>
        /// Ticket mit der angegebenen ID finden
        /// </summary>
        /// <param name="session"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        private Ticket GetTicket(ISessionTransaction session, int id)
        {
            Ticket ticket = Ticket.QueryExtent(session.Broker, "OID ==" + id).FirstOrDefault();
            if (ticket == null)
                throw new Exception("Kein Ticket mit der Ticketnummer #" + id.ToString() + " vorhanden.");
            return ticket;
        }
    }
}
