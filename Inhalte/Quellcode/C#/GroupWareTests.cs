using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using dotNetBF.Core;
using dotNetBF.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GeoMan.GroupWare
{
	[TestClass]
	public class Tests : TestBase
	{
		private Bmm.Obj.Device m_device;
		private Facilities.FacilityObject m_fo;
		private Bmm.Erp.DeviceOrder m_do;
		private Server m_server;

		/// <summary>
		/// Räumt die DB auf, wenn AutoCleanupDb == true (Standardfall)
		/// </summary>
		[TestCleanup]
		public void TestCleanup()
		{
			Cleanup();
		}

		[TestMethod]
		[TestCategory("GeoMan.GroupWare")]
		[Owner("KMS_HW")]
		[Priority(9)]
		public void TestGroupWare()
		{
			using (var session = OpenSession("user", "fmgis"))
			{
				CreateServer(session); // Groupware Server anlegen (MS Exchange)
				ImportTestData(session); // Import: 1 Gerät inkl. Struktur + 1 Liegenschaft / 1 Maßnahme zum Gerät anlegen

				CreateEventForDevice(session); // Event: Fabrikatnummer des Gerätes hat sich geändert
				FireEventForDevice(session);

				CreateEventForFo(session); // Event: Anzahl Stellplätze der Liegenschaft hat sich geändert
				FireEventForFo(session);

				CreateEventForOrder(session); // Event: Status der Maßnahme (am o.a. Gerät) hat sich geändert
				FireEventForOrder(session);

				Thread.Sleep(15000); // wait for execution

				CheckLogMail(session); // Log-Einträge + evtl. Fehlermeldungen
			}
		}

		// Broken Rules Exception, wenn keine Verbindung möglich
		private void CreateServer(ISession session)
		{
			foreach (Server sx in Server.QueryExtent(session.Broker, "UserName like 'testuser2'"))
			{
				foreach (Event ev in sx.Events)
					session.Broker.Delete(ev);

				session.Broker.Delete(sx);
			}

			try
			{
				session.Commit();
			}
			catch (Exception e)
			{
				Assert.Fail(e.Message);
			}

            m_server = TestHelper.CreateExchangeServer(session);

            try
			{
				session.Commit();
			}
			catch (Exception e)
			{
				Assert.Fail(e.Message);
			}
		}

		private void ImportTestData(ISession session)
		{
			m_device = Bmm.Obj.Device.QueryExtent(
				session.Broker,
				"ImportID like 'gw0001' && Status != param2",
				new QueryParameter("param2", Bmm.Obj.KnownAssetStates.Invalid)).First;

			m_fo = Facilities.FacilityObject.QueryExtent(session.Broker, "AssetNoExt like 'GroupWare001'").First;

			if (m_device == null || m_fo == null)
			{
				DirectoryInfo di = new DirectoryInfo("./GroupWare/ImportFiles");
				Assert.IsTrue(di.Exists, "Ausgangsdaten-Ordner \"" + di.FullName + "\" nicht vorhanden!");
				var files = di.GetFiles("*.xls").OrderBy(x => x.Name);
				Assert.IsTrue(files.Any(), "Ausgangsdaten im Ordner \"" + Environment.CurrentDirectory + "\\ImportFiles\" nicht vorhanden!");
				var result = Common.Core.ExcelImport.Import(session, files.Select(x => new KeyValuePair<string, string>(x.Name, x.FullName)).ToArray());
				string message = "Import TestDaten fehlgeschlagen!" + Environment.NewLine
								 + (result.ResultText ?? string.Empty) + Environment.NewLine
								 + (result.ResultGroupText ?? string.Empty) + Environment.NewLine
								 + string.Join(Environment.NewLine, result.BrokenRules.Select(x => x.Message));
				Assert.IsTrue(result.Sucess, message);

				m_device = Bmm.Obj.Device.QueryExtent(
					session.Broker,
					"ImportID like 'gw0001' && Status != param2",
					new QueryParameter("param2", Bmm.Obj.KnownAssetStates.Invalid)).First;

				m_fo = Facilities.FacilityObject.QueryExtent(session.Broker, "AssetNoExt like 'GroupWare001'").First;
			}
			Assert.IsNotNull(m_device, "Testgerät GroupWare nicht gefunden");
			Assert.IsNotNull(m_fo, "Testliegenschaft GroupWare nicht gefunden");

			// Maßnahme zu Testgerät
			m_do = Bmm.Erp.DeviceOrder.Create(session.Broker);
			m_do.MaintenableObject = m_device;
			m_do.RepairType = Bmm.Prc.RepairType.QueryExtent(session.Broker, "Name like 'TestArt'").First;
			Assert.IsNotNull(m_do.RepairType, "Maßnahmenart TestArt nicht gefunden");
			m_do.BeginOrder = DateTime.Now;
			m_do.Cause = "TestMaßnahme GroupWare";

			try
			{
				session.Commit();
			}
			catch (Exception e)
			{
				Assert.Fail(e.Message);
			}
		}

		private void CreateEventForDevice(ISession session)
		{
			foreach (Event ex in Event.QueryExtent(session.Broker, "Description like 'Unittest GroupWare Device'"))
				session.Broker.Delete(ex);

			session.Commit();

			Event e = Event.Create(session.Broker);
			e.EventType = Common.Core.KnownObjects.GroupwareMail;
			e.Description = "Unittest GroupWare Device";
			e.Category = Devices.KnownObjects.MainCategory;
			e.DataType = Devices.KnownObjects.DeviceDataType;
			e.IsPropChanged = true;

			FeatureTrigger t = FeatureTrigger.Create(session.Broker);
			t.FeatureName = "SerialNoExt";
			e.FeatureTriggers.Add(t);

			//e.GroupwareEmail wird durch Event by EventType "GroupwareMail" angelegt
			e.GroupwareEmail.Subject = "Unittest GroupWare Device-Event [sernoext]";
			e.GroupwareEmail.Body = "Unittest GroupWare Device Event: SerialNoExt [sernoext] (Fabrikatnummer) geändert";
			Recipient r = Recipient.Create(session.Broker);
			r.Value = "testuser2@kms-computer.de";
			e.GroupwareEmail.Recipients.Add(r);

			Param par = Param.Create(session.Broker);
			par.Name = "sernoext";
			par.FeatureName = "SerialNoExt";
			e.Params.Add(par);

			m_server.Events.Add(e);

			try
			{
				session.Commit();
			}
			catch (Exception ex)
			{
				Assert.Fail(ex.Message);
			}
		}

		private void CreateEventForFo(ISession session)
		{
			foreach (Event ex in Event.QueryExtent(session.Broker, "Description like 'Unittest GroupWare FO'"))
				session.Broker.Delete(ex);

			session.Commit();

			Event e = Event.Create(session.Broker);
			e.EventType = Common.Core.KnownObjects.GroupwareMail;
			e.Description = "Unittest GroupWare FO";
			e.Category = Facilities.KnownReports.MainCategory;
			e.DataType = Facilities.KnownDataTypes.RealestateDataType;
			e.IsPropChanged = true;

			FeatureTrigger t = FeatureTrigger.Create(session.Broker);
			t.FeatureName = "Parkinglots";
			e.FeatureTriggers.Add(t);

			//e.GroupwareEmail wird durch Event by EventType "GroupwareMail" angelegt
			e.GroupwareEmail.Subject = "Unittest GroupWare FacilityData-Event [parklots]";
			e.GroupwareEmail.Body = "Unittest GroupWare FacilityData Event: Parkinglots [parklots] (Stellplätze) geändert";
			Recipient r = Recipient.Create(session.Broker);
			r.Value = "testuser2@kms-computer.de";
			e.GroupwareEmail.Recipients.Add(r);

			Param par = Param.Create(session.Broker);
			par.Name = "parklots";
			par.FeatureName = "Parkinglots";
			e.Params.Add(par);

			m_server.Events.Add(e);

			try
			{
				session.Commit();
			}
			catch (Exception ex)
			{
				Assert.Fail(ex.Message);
			}
		}

		private void CreateEventForOrder(ISession session)
		{
			foreach (Event ex in Event.QueryExtent(session.Broker, "Description like 'Unittest GroupWare Order'"))
				session.Broker.Delete(ex);

			session.Commit();

			Event e = Event.Create(session.Broker);
			e.EventType = Common.Core.KnownObjects.GroupwareMail;
			e.Description = "Unittest GroupWare Order";
			e.Category = Devices.KnownObjects.MainCategory;
			e.DataType = Devices.KnownObjects.DeviceOrderDataType;
			e.IsPropChanged = true;

			FeatureTrigger t = FeatureTrigger.Create(session.Broker);
			t.FeatureName = "Status";
			e.FeatureTriggers.Add(t);

			//e.GroupwareEmail wird durch Event by EventType "GroupwareMail" angelegt
			e.GroupwareEmail.Subject = "Unittest GroupWare DeviceOrder-Event [statx]";
			e.GroupwareEmail.Body = "Unittest GroupWare DeviceOrder-Event: Status [statx] geändert";
			Recipient r = Recipient.Create(session.Broker);
			r.Value = "testuser2@kms-computer.de";
			e.GroupwareEmail.Recipients.Add(r);

			Param par = Param.Create(session.Broker);
			par.Name = "statx";
			par.FeatureName = "Status";
			e.Params.Add(par);

			m_server.Events.Add(e);

			try
			{
				session.Commit();
			}
			catch (Exception ex)
			{
				Assert.Fail(ex.Message);
			}
		}

		private void FireEventForDevice(ISession session)
		{
			if (string.IsNullOrEmpty(m_device.SerialNoExt) || m_device.SerialNoExt == "testgw2")
				m_device.SerialNoExt = "testgw1";
			else
				m_device.SerialNoExt = "testgw2";
			try
			{
				session.Commit();
			}
			catch (Exception e)
			{
				Assert.Fail(e.Message);
			}
		}

		private void FireEventForFo(ISession session)
		{
			if (ObjectBroker.IsNullValue(m_fo.Realestate.Parkinglots) || m_fo.Realestate.Parkinglots == 33)
				m_fo.Realestate.Parkinglots = 22;
			else
				m_fo.Realestate.Parkinglots = 33;
			try
			{
				session.Commit();
			}
			catch (Exception e)
			{
				Assert.Fail(e.Message);
			}
		}

		private void FireEventForOrder(ISession session)
		{
			if (m_do.Status == Bmm.Erp.OrderStateMachine.Planned)
				m_do.Status = Bmm.Erp.OrderStateMachine.Done;
			else
				m_do.Status = Bmm.Erp.OrderStateMachine.Planned;
			try
			{
				session.Commit();
			}
			catch (Exception e)
			{
				Assert.Fail(e.Message);
			}
		}

		private void CheckLogMail(ISession session)
		{
			LogMail lm = LogMail.QueryExtent(session.Broker, "Date > System.DateTime.Now.AddSeconds(-60) && Object like param1", new QueryParameter("param1", m_device.OPath.ToString())).First;
			Assert.IsNotNull(lm, "Kein Logeintrag für Gerät mit OPath " + m_device.OPath);
			Assert.IsNull(lm.Message, "Log Fehler Email Gerät: " + lm.Message);

			lm = LogMail.QueryExtent(session.Broker, "Date > System.DateTime.Now.AddSeconds(-60) && Object like param1", new QueryParameter("param1", m_fo.Realestate.OPath.ToString())).First;
			Assert.IsNotNull(lm, "Kein Logeintrag für Liegenschaft mit OPath " + m_fo.Realestate.OPath);
			Assert.IsNull(lm.Message, "Log Fehler Email Liegenschaft: " + lm.Message);

			lm = LogMail.QueryExtent(session.Broker, "Date > System.DateTime.Now.AddSeconds(-60) && Object like param1", new QueryParameter("param1", m_do.OPath.ToString())).First;
			Assert.IsNotNull(lm, "Kein Logeintrag für Maßnahme mit OPath " + m_do.OPath);
			Assert.IsNull(lm.Message, "Log Fehler Email Maßnahme: " + lm.Message);
		}
	}
}