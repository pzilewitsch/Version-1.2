using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bmm.Erp;
using Bmm.Prc;
using dotNetBF.Core;
using dotNetBF.Core.Rules;
using dotNetBF.Modules.Accounting;
using dotNetBF.Modules.Org;
using dotNetBF.Modules.Services.Security;
using dotNetBF.Services;
using GeoMan.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using dotNetBF.Modules.Services.Documents;

namespace GeoMan.ServiceDesk
{
	[TestClass]
	public partial class Tests : TestBase
	{
		private Type m_ticketType, m_templateType;
		private Person m_person;
		private Facilities.FacilityObject m_building;
		private Facilities.FacilityObjectOrder m_order;
		private TicketTemplate m_ticketTemplate;
		private Trade m_ticketTrade, m_templateTrade;
		private Currency m_currency;
		private RepairType m_reptype;
		private string m_ticketEmail, m_templateEmail;
		private Org m_ticketOrganisation, m_templateOrganisation;

		/// <summary>
		/// Räumt die DB auf, wenn AutoCleanupDb == true (Standardfall)
		/// </summary>
		[TestCleanup]
		public void TestCleanup()
		{
			Cleanup();
		}

		[TestMethod]
		[TestCategory("GeoMan.ServiceDesk")]
		[Owner("KMS_AG")]
		[Priority(1)]
		public void TestTicket()
		{
			using (var session = OpenSession("user", "fmgis"))
			{
				Create_Data(session); //Testdaten anlegen
				TestTicket(session); //Ticket/Antwort-Tests
				TestTicketTemplate(session); //Meldungsvorlage-Tests
			}
		}

		private void Create_Data(ISession session)
		{
			var broker = session.Broker;

			// Meldungsarten für Meldung und Meldungsvorlage
			m_ticketType = QueryOrCreateTicketType(broker, "ticketTypeName");
			m_templateType = QueryOrCreateTicketType(broker, "templateTypeName");

			// Person
			m_person = QueryOrCreatePerson(broker, "Testperson");

			// Gewerke für Meldung und Meldungsvorlage
			m_ticketTrade = QueryOrCreateTicketTrade(broker, "ticketTradeName");
			m_templateTrade = QueryOrCreateTicketTrade(broker, "templateTradeName");

			// Currency
			m_currency = Currency.QueryExtent(broker).FirstOrDefault() ?? Currency.Create(broker);

			// Organisationen für Meldung und Meldungsvorlage
			m_ticketOrganisation = QueryOrCreateTicketOrganisation(broker, "ticketOrganisationName");
			m_templateOrganisation = QueryOrCreateTicketOrganisation(broker, "templateOrganisationName");

			// Emails für Meldung und Meldungsvorlage
			m_ticketEmail = "TestEmail@Ticket.test";
			m_templateEmail = "TestEmail@TicketTemplate.test";

			// Meldungsvorlage
			m_ticketTemplate = CreateTicketTemplate(broker);

			//Adresse anlegen
			TestHelper.CreateGermanDefaultAddress(session);

			// Gebäudeimport
			m_building = Facilities.FacilityObject.Query(session.Broker).Where(x => x.Building != null).Limit(1).Execute().First
						 ?? ImportBuilding(session);

			// RepairType
			m_reptype = QueryOrCreateRepairType(broker);

			// Maßnahme
			m_order = NewOrder(session);

			session.Commit();
		}

		private Type QueryOrCreateTicketType(IObjectBroker broker, string name)
		{
			var result = Type.QueryExtent(broker, "Name == objName", new QueryParameter("objName", name)).First ?? Type.Create(broker);
			if (string.IsNullOrWhiteSpace(result.Name))
				result.Name = name;

			return result;
		}

		private Trade QueryOrCreateTicketTrade(IObjectBroker broker, string name)
		{
			var result = Trade.QueryExtent(broker, "Name == objName", new QueryParameter("objName", name)).First ?? Trade.Create(broker);
			if (string.IsNullOrWhiteSpace(result.Name))
				result.Name = name;

			return result;
		}

		private Org QueryOrCreateTicketOrganisation(IObjectBroker broker, string name)
		{
			var result = Org.QueryExtent(broker, "Name == objName", new QueryParameter("objName", name)).First ?? Org.Create(broker);
			if (string.IsNullOrWhiteSpace(result.Name))
				result.Name = name;

			var typeName = name + "OfOrgType";
			result.Type = OrgType.QueryExtent(broker, "Name == objName", new QueryParameter("objName", typeName)).First ?? OrgType.Create(broker);
			if (string.IsNullOrWhiteSpace(result.Type.Name))
				result.Type.Name = typeName;

			return result;
		}

		private TicketTemplate CreateTicketTemplate(IObjectBroker broker)
		{
			var result = TicketTemplate.Create(broker);
			result.Trade = m_templateTrade;
			result.Type = m_templateType;
			result.Message = Guid.NewGuid().ToString();
			result.Description = Guid.NewGuid().ToString();
			result.Org = m_templateOrganisation;
			result.Email = m_templateEmail;

			return result;
		}

		private Facilities.FacilityObject ImportBuilding(ISession session)
		{
			DirectoryInfo di = new DirectoryInfo("./ServiceDesk/ImportFiles");
			Assert.IsNotNull(di.Exists, "Ausgangsdaten-Ordner \"" + di.FullName + "\" nicht vorhanden!");

			var files = di.GetFiles("*.xls").OrderBy(x => x.Name);
			Assert.IsTrue(files.Any(), "Ausgangsdaten im Ordner \"" + Environment.CurrentDirectory + "\\ImportFiles\" nicht vorhanden!");

			var result = Common.Core.ExcelImport.Import(session, files.Select(x => new KeyValuePair<string, string>(x.Name, x.FullName)).ToArray());
			string message = "Import TestDaten fehlgeschlagen!" + Environment.NewLine
							 + (result.ResultText ?? string.Empty) + Environment.NewLine
							 + (result.ResultGroupText ?? string.Empty) + Environment.NewLine
							 + string.Join(Environment.NewLine, result.BrokenRules.Select(x => x.Message));
			Assert.IsTrue(result.Sucess, message);

			return Facilities.FacilityObject.Query(session.Broker).Where(x => x.Building != null).Limit(1).Execute().First;
		}

		private RepairType QueryOrCreateRepairType(IObjectBroker broker)
		{
			var result = RepairType.Query(broker).Limit(1).Execute().First;
			if (result == null)
			{
				result = RepairType.Create(broker);
				result.Name = Guid.NewGuid().ToString();
				result.ShortName = m_reptype.Name;
			}

			return result;
		}

		private Person QueryOrCreatePerson(IObjectBroker broker, string name)
		{
			var result = SecurityService.CurrentUser.Person
						 ?? Person.Create(broker);

			if (string.IsNullOrWhiteSpace(result.Name))
				result.Name = name;

			return result;
		}

		private Ticket NewTicket(ISession session)
		{
			Ticket ticket = Ticket.Create(session.Broker);
			ticket.Message = "TestMeldung";
			ticket.Type = m_ticketType;
			ticket.Building = m_building;
			ticket.Status = KnownObjects.Untouched;
			return ticket;
		}

		private Answer NewAnswer(ISession session)
		{
			Answer a = Answer.Create(session.Broker);
			a.NewRespPerson = m_person;
			return a;
		}

		private void AssertEqual(Ticket ticket, Status status, int test)
		{
			Assert.AreEqual(ticket.Status, status, "Test " + test + "=> Ticket hat falschen Status: '" + ticket.Status.Name + "' (Erwartet : '" + status.Name + "')");
		}

		private Facilities.FacilityObjectOrder NewOrder(ISession session)
		{
			var o = Facilities.FacilityObjectOrder.Create(session.Broker);
			o.Texts.Cause = "Testmaßnahme";
			o.RepairType = m_reptype;
			o.MaintenableObject = m_building;
			o.PlannedRepairs[0].PlannedDate = DateTime.Today.AddDays(1);
			o.Status = OrderStateMachine.Planned;
			o.IsVisible = true;
			return o;
		}

		private OrderProcCustom NewOrderProcCustom(ISession session)
		{
			var o = OrderProcCustom.Create(session.Broker);
			o.Status = OrderProcessStateMachine.Work;
			var current = OrderProcPers.Create(session.Broker);
			o.PlannedPers.Add(current);
			o.PlannedPers[0].Person = m_person;
			return o;
		}

		private Devices.OrderProcOutsourced NewOrderProcOutsourced(ISession session)
		{
			var o = Devices.OrderProcOutsourced.Create(session.Broker);
			o.Status = OrderProcessStateMachine.Planned;
			var current = OrderProcPers.Create(session.Broker);
			o.PlannedPers.Add(current);
			o.PlannedPers[0].Person = m_person;
			o.Supplier = m_person;
			o.BeginDate = DateTime.Today;
			o.AmountType = Common.Core.PaymentService.Net;
			o.Currency = m_currency;
			return o;
		}

		private void TestTicket(ISession session)
		{
			var i = 1;
			//Test 1: Anlegen
			Ticket ticket = NewTicket(session);
			session.Commit();
			AssertEqual(ticket, KnownObjects.Untouched, i++);

			//Test 2: neue Antwort
			Answer a = NewAnswer(session);
			a.Ticket = ticket;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 3: Antwort auf Fertig
			a.Done = true;
			session.Commit();
			AssertEqual(ticket, KnownObjects.Completed, i++);

			//Test 4: 2. Antwort (wiedereröffnet)
			a = NewAnswer(session);
			a.Ticket = ticket;
			a.Reopen = true;
			session.Commit();
			AssertEqual(ticket, KnownObjects.ReOpened, i++);

			//Test 5: 3. Antwort (fertig)
			a = NewAnswer(session);
			a.Ticket = ticket;
			a.Done = true;
			session.Commit();
			AssertEqual(ticket, KnownObjects.Completed, i++);

			//Test 6: Anlegen
			ticket = NewTicket(session);
			session.Commit();

			ticket.Order = m_order;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 7: Antwort + Maßnahme
			ticket = NewTicket(session);
			session.Commit();

			a = NewAnswer(session);
			a.Ticket = ticket;
			session.Commit();

			ticket.Order = m_order;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 8: Antwort (fertig) + Maßnahme 
			a.Done = true;
		    try
		    {
                session.Commit();
		    }
		    catch (BrokenRuleException ex)
		    {
                Assert.IsTrue(ex.BrokenRules.Count == 1 && ex.BrokenRules[0].Name == "DoneRule", "Test 8 => Unerwartet BrokenRules: " + ex.Message);
                a.Done = false;
		    }
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 9: Maßnahme + Antwort
			ticket = NewTicket(session);
			session.Commit();

			ticket.Order = m_order;
			session.Commit();

			a = NewAnswer(session);
			a.Ticket = ticket;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 10: Maßnahme + Antwort (fertig)
			a.Done = true;
            try
            {
                session.Commit();
            }
            catch (BrokenRuleException ex)
            {
                Assert.IsTrue(ex.BrokenRules.Count == 1 && ex.BrokenRules[0].Name == "DoneRule", "Test 8 => Unerwartet BrokenRules: " + ex.Message);
                a.Done = false;
            }
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 11
			i++;

			//Test 12: Maßnahme (in Arbeit)
			m_order = NewOrder(session);
			ticket = NewTicket(session);
			session.Commit();

			ticket.Order = m_order;
			m_order.Status = OrderStateMachine.Work;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 13: Maßnahme (technisch Fertig)
			m_order.Status = OrderStateMachine.TecDone;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 14: Maßnahme (Fertig)
			m_order.Status = OrderStateMachine.Done;
			session.Commit();
			AssertEqual(ticket, KnownObjects.Done, i++);

			//Test 15: Maßnahme (Fertig)
			a = NewAnswer(session);
			a.Ticket = ticket;
			a.Done = true;
			session.Commit();
			AssertEqual(ticket, KnownObjects.Completed, i++);

			//Test 16: Maßnahme mit Arbeitsauftrag (in Arbeit)
			ticket = NewTicket(session);
			m_order = NewOrder(session);
			ticket.Order = m_order;
			session.Commit();

			OrderProcCustom orderprocCustom = NewOrderProcCustom(session);
			orderprocCustom.Order = m_order;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 17: Maßnahme mit Arbeitsauftrag (übergeben)
			orderprocCustom.Status = Devices.OrderProcessStateMachine.Deliverd;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 18: Maßnahme mit Arbeitsauftrag (abgelehnt)
			orderprocCustom.Status = Devices.OrderProcessStateMachine.Declined;
			session.Commit();
			AssertEqual(ticket, KnownObjects.Done, i++);

			//Test 19: Maßnahme mit Arbeitsauftrag (fertig)
			ticket = NewTicket(session);
			m_order = NewOrder(session);
			ticket.Order = m_order;
			session.Commit();

			orderprocCustom = NewOrderProcCustom(session);
			orderprocCustom.Order = m_order;
			orderprocCustom.Status = OrderProcessStateMachine.Done;
			session.Commit();
			AssertEqual(ticket, KnownObjects.Done, i++);

			//Test 20: Maßnahme mit Fremdvergabe (In Planung)
			ticket = NewTicket(session);
			m_order = NewOrder(session);
			ticket.Order = m_order;
			session.Commit();

			Devices.OrderProcOutsourced orderprocOutsourced = NewOrderProcOutsourced(session);
			orderprocOutsourced.Order = m_order;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 21: Maßnahme mit Fremdvergabe (In Arbeit)
			orderprocOutsourced.Status = OrderProcessStateMachine.Work;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 22: Maßnahme mit Fremdvergabe (Fertig)
			orderprocOutsourced.Status = OrderProcessStateMachine.Done;
			session.Commit();
			AssertEqual(ticket, KnownObjects.Done, i++);

			//Test 23: Maßnahme mit Arbeitsauftrag (in Arbeit) und Fremdvergabe (In Planung)
			ticket = NewTicket(session);
			m_order = NewOrder(session);
			ticket.Order = m_order;
			session.Commit();

			orderprocOutsourced = NewOrderProcOutsourced(session);
			orderprocOutsourced.Order = m_order;
			orderprocCustom = NewOrderProcCustom(session);
			orderprocCustom.Order = m_order;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 24: Maßnahme mit Arbeitsauftrag (übergeben) und Fremdvergabe (In Planung)
			orderprocCustom.Status = Devices.OrderProcessStateMachine.Deliverd;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 25: Maßnahme mit Arbeitsauftrag (abgelehnt) und Fremdvergabe (In Planung)
			orderprocCustom.Status = Devices.OrderProcessStateMachine.Declined;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 26: Maßnahme mit Arbeitsauftrag (fertig) und Fremdvergabe (In Planung)
			ticket = NewTicket(session);
			m_order = NewOrder(session);
			ticket.Order = m_order;
			session.Commit();

			orderprocOutsourced = NewOrderProcOutsourced(session);
			orderprocOutsourced.Order = m_order;
			orderprocCustom = NewOrderProcCustom(session);
			orderprocCustom.Order = m_order;
			orderprocCustom.Status = OrderProcessStateMachine.Done;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 27: Maßnahme mit Arbeitsauftrag (in Arbeit) und Fremdvergabe (In Arbeit)
			ticket = NewTicket(session);
			m_order = NewOrder(session);
			ticket.Order = m_order;
			session.Commit();

			orderprocOutsourced = NewOrderProcOutsourced(session);
			orderprocOutsourced.Order = m_order;
			orderprocOutsourced.Status = OrderProcessStateMachine.Work;
			orderprocCustom = NewOrderProcCustom(session);
			orderprocCustom.Order = m_order;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 28: Maßnahme mit Arbeitsauftrag (übergeben) und Fremdvergabe (In Arbeit)
			orderprocCustom.Status = Devices.OrderProcessStateMachine.Deliverd;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 29: Maßnahme mit Arbeitsauftrag (abgelehnt) und Fremdvergabe (In Arbeit)
			orderprocCustom.Status = Devices.OrderProcessStateMachine.Declined;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 30: Maßnahme mit Arbeitsauftrag (fertig) und Fremdvergabe (In Arbeit)
			ticket = NewTicket(session);
			m_order = NewOrder(session);
			ticket.Order = m_order;
			session.Commit();

			orderprocOutsourced = NewOrderProcOutsourced(session);
			orderprocOutsourced.Order = m_order;
			orderprocOutsourced.Status = OrderProcessStateMachine.Work;
			orderprocCustom = NewOrderProcCustom(session);
			orderprocCustom.Order = m_order;
			orderprocCustom.Status = OrderProcessStateMachine.Done;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 31: Maßnahme mit Arbeitsauftrag (in Arbeit) und Fremdvergabe (Fertig)
			ticket = NewTicket(session);
			m_order = NewOrder(session);
			ticket.Order = m_order;
			session.Commit();

			orderprocOutsourced = NewOrderProcOutsourced(session);
			orderprocOutsourced.Order = m_order;
			orderprocOutsourced.Status = OrderProcessStateMachine.Done;
			orderprocCustom = NewOrderProcCustom(session);
			orderprocCustom.Order = m_order;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 32: Maßnahme mit Arbeitsauftrag (übergeben) und Fremdvergabe (Fertig)
			orderprocCustom.Status = Devices.OrderProcessStateMachine.Deliverd;
			session.Commit();
			AssertEqual(ticket, KnownObjects.InWork, i++);

			//Test 33: Maßnahme mit Arbeitsauftrag (abgelehnt) und Fremdvergabe (Fertig)
			orderprocCustom.Status = Devices.OrderProcessStateMachine.Declined;
			session.Commit();
			AssertEqual(ticket, KnownObjects.Done, i++);

			//Test 34: Maßnahme mit Arbeitsauftrag (fertig) und Fremdvergabe (Fertig)
			ticket = NewTicket(session);
			m_order = NewOrder(session);
			ticket.Order = m_order;
			session.Commit();

			orderprocOutsourced = NewOrderProcOutsourced(session);
			orderprocOutsourced.Order = m_order;
			orderprocOutsourced.Status = OrderProcessStateMachine.Done;
			orderprocCustom = NewOrderProcCustom(session);
			orderprocCustom.Order = m_order;
			orderprocCustom.Status = OrderProcessStateMachine.Done;
			session.Commit();
			AssertEqual(ticket, KnownObjects.Done, i++);

			//Test 35 
			i++;

			//Test 36 : Ticket kopieren 1
			ticket = NewTicket(session);
			session.Commit();
			Ticket newTicket = NewTicket(session);
			Services.Copy(ticket, newTicket);
			session.Commit();
			AssertEqual(newTicket, KnownObjects.Untouched, i++);

			//Test 37 
			i++;

			//Test 38 : Ticket kopieren 2
			ticket = NewTicket(session);
			ticket.Status = KnownObjects.InWork;
			session.Commit();
			newTicket = NewTicket(session);
			Services.Copy(ticket, newTicket);
			session.Commit();
			AssertEqual(newTicket, KnownObjects.Untouched, i++);

			//Test 39 
			i++;

			//Test 40 : Ticket kopieren 3
			ticket = NewTicket(session);
			ticket.Status = KnownObjects.Done;
			session.Commit();
			newTicket = NewTicket(session);
			Services.Copy(ticket, newTicket);
			session.Commit();
			AssertEqual(newTicket, KnownObjects.Untouched, i++);

			//Test 41 
			i++;

			//Test 42 : Ticket kopieren 4
			ticket = NewTicket(session);
			ticket.Status = KnownObjects.Completed;
			session.Commit();
			newTicket = NewTicket(session);
			Services.Copy(ticket, newTicket);
			session.Commit();
			AssertEqual(newTicket, KnownObjects.Untouched, i++);
		}

		private void TestTicketTemplate(ISession session)
		{
			dotNetBF.Gui.GuiObject guiObject = null;
			var msg = Guid.NewGuid().ToString();

			//Test 5-8 Vorlage verwenden
			Ticket ticket = Ticket.Create(session.Broker);
			ticket.Building = m_building;
			ticket.Status = KnownObjects.Untouched;
			guiObject = session.GuiRepository.Build(session.Broker, ticket);
			guiObject.SetValue("TicketTemplate", m_ticketTemplate);
			session.Commit();

			Assert.AreEqual(ticket.Type, m_ticketTemplate.Type, "Art unterscheidet sich von der angehängten Meldungsvorlage");
			Assert.AreEqual(ticket.Trade, m_ticketTemplate.Trade, "Gewerk unterscheidet sich von der angehängten Meldungsvorlage");
			Assert.AreEqual(ticket.Message, m_ticketTemplate.Message, "Meldungstext unterscheidet sich von der angehängten Meldungsvorlage");
			Assert.AreEqual(ticket.Description, m_ticketTemplate.Description, "Gewerk unterscheidet sich von der angehängten Meldungsvorlage");
			Assert.AreEqual(ticket.Org, m_ticketTemplate.Org, "Organisation unterscheidet sich von der angehängten Meldungsvorlage");
			Assert.AreEqual(ticket.EMail, m_ticketTemplate.Email, "Email unterscheidet sich von der angehängten Meldungsvorlage");

			//Test 9-12 Daten befüllen dann Vorlage verwenden
			ticket = Ticket.Create(session.Broker);
			ticket.Building = m_building;
			ticket.Status = KnownObjects.Untouched;
			guiObject = session.GuiRepository.Build(session.Broker, ticket);
			guiObject.SetValue("Type", m_ticketType);
			guiObject.SetValue("Trade", m_ticketTrade);
			guiObject.SetValue("Message", msg);
			guiObject.SetValue("Description", msg);
			guiObject.SetValue("Org", m_ticketOrganisation);
			guiObject.SetValue("EMail", m_ticketEmail);
			guiObject.SetValue("TicketTemplate", m_ticketTemplate);
			session.Commit();

			Assert.AreEqual(ticket.Type, m_ticketTemplate.Type, "Art unterscheidet sich von der angehängten Meldungsvorlage");
			Assert.AreEqual(ticket.Trade, m_ticketTemplate.Trade, "Gewerk unterscheidet sich von der angehängten Meldungsvorlage");
			Assert.AreEqual(ticket.Message, m_ticketTemplate.Message, "Meldungstext unterscheidet sich von der angehängten Meldungsvorlage");
			Assert.AreEqual(ticket.Description, m_ticketTemplate.Description, "Gewerk unterscheidet sich von der angehängten Meldungsvorlage");
			Assert.AreEqual(ticket.Org, m_ticketTemplate.Org, "Organisation unterscheidet sich von der angehängten Meldungsvorlage");
			Assert.AreEqual(ticket.EMail, m_ticketTemplate.Email, "Email unterscheidet sich von der angehängten Meldungsvorlage");

			//Test 13-16 Vorlage verwenden dann Daten befüllen
			ticket = Ticket.Create(session.Broker);
			ticket.Building = m_building;
			ticket.Status = KnownObjects.Untouched;
			guiObject = session.GuiRepository.Build(session.Broker, ticket);
			guiObject.SetValue("TicketTemplate", m_ticketTemplate);
			guiObject.SetValue("Type", m_ticketType);
			guiObject.SetValue("Trade", m_ticketTrade);
			guiObject.SetValue("Message", msg);
			guiObject.SetValue("Description", msg);
			guiObject.SetValue("Org", m_ticketOrganisation);
			guiObject.SetValue("EMail", m_ticketEmail);
			session.Commit();

			Assert.AreEqual(ticket.Type, m_ticketType, "Art wurde fälschlicherweise von der Meldungsvorlage überschrieben");
			Assert.AreEqual(ticket.Trade, m_ticketTrade, "Gewerk wurde fälschlicherweise von der Meldungsvorlage überschrieben");
			Assert.AreEqual(ticket.Message, msg, "Meldungstext wurde fälschlicherweise von der Meldungsvorlage überschrieben");
			Assert.AreEqual(ticket.Description, msg, "Gewerk wurde fälschlicherweise von der Meldungsvorlage überschrieben");
			Assert.AreEqual(ticket.Org, m_ticketOrganisation, "Organisation wurde fälschlicherweise von der Meldungsvorlage überschrieben");
			Assert.AreEqual(ticket.EMail, m_ticketEmail, "Email wurde fälschlicherweise von der Meldungsvorlage überschrieben");
		}

        //Testmethode zum Überprüfen der E-Mail-Integration
        private void TestMailToObject(ISession session)
        {
            try
            {   
                //Server & Service erstellen und anschließend Mail senden
                var server = TestHelper.CreateExchangeServer(session);
                var service = TestHelper.CreateExchangeService(server);
                TestHelper.SendMail(service);
            }
            catch (Exception)
            {
                Assert.Fail("Fehler beim Versenden der Mail");
            }

            //Service MailsToObject einleiten
            var ticketService = Module.GetService<TicketHandler>();
            ticketService.MailsToObjects();

            //Prüfen, ob Meldung vom Service angelegt wurde
            var result = Ticket.QueryExtent(session.Broker, "Message = 'Testmeldung'").First();
            if (result == null)
                Assert.Fail("Meldung wurde nicht erfolgreich angelegt");

            //Prüfen, ob Anhang als Dokument angelegt wurde
            var doc = Document.QueryExtent(session.Broker, "Ticket = ticketId", new QueryParameter("ticketId", result.OID)).First();
            if (doc == null)
                Assert.Fail("Anhang wurde nicht als Dokument angelegt");
        }
	}
}