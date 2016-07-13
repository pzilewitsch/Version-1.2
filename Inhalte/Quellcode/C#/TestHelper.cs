using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using dotNetBF.Core;
using dotNetBF.Modules.Accounting;
using dotNetBF.Modules.Geo;
using dotNetBF.Services;
using GeoMan.Accounting.Payments;
using GeoMan.Address;
using Microsoft.Exchange.WebServices.Data;

namespace GeoMan
{
	public static class TestHelper
	{
		public static Contracts.Contract CreateContract(ISession session, Contracts.ContractClass contractClass, DateTime contractBeginn, Contracts.ContractClass2EntityType objectType, Contracts.ContractRentalType rentalType)
		{
			Contracts.Contract contract = Contracts.Contract.Create(session.Broker);
			contract.ContractClass = contractClass;
			contract.ContractNumber = DateTime.Now.Ticks.ToString();
			contract.ContractName = DateTime.Now.Ticks.ToString();
			contract.ContractBegin = DateTime.Today;
			contract.ContractObjectType = objectType;
			contract.ContractRentalType = rentalType;
			contract.IsPermanentContract = true;
			contract.ContractState = Contracts.KnownObjects.Earmarked;
			contract.Country = Country.QueryExtent(session.Broker, "Name == 'Deutschland'").First;

			dotNetBF.Modules.Org.Person renter = CreatePerson(session);
			dotNetBF.Modules.Org.Person lessor = CreatePerson(session);

			contract.ContractPayer = renter;
			contract.ContractPayee = lessor;

			return contract;
		}

		/// <summary>
		/// Erstellt eine Liegenschaft mit Nummer und Bezeichnung aus DateTime.Now.Ticks
		/// </summary>
		/// <param name="session"></param>
		/// <returns></returns>
		public static Facilities.FacilityObject CreateRealestate(ISession session)
		{
			Facilities.FacilityObject realestate = Facilities.FacilityObject.Create(session.Broker);
			realestate.ObjectType = Facilities.KnownObjects.RealestateObjectType;
			realestate.AssetNo = DateTime.Now.Ticks.ToString();
			realestate.Name = DateTime.Now.Ticks.ToString();

			return realestate;
		}

		/// <summary>
		/// Erstellt ein Gebäude mit Nummer und Bezeichnung aus DateTime.Now.Ticks
		/// </summary>
		/// <param name="session"></param>
		/// <returns></returns>
		public static Facilities.FacilityObject CreateBuilding(ISession session)
		{
			Facilities.FacilityObject building = Facilities.FacilityObject.Create(session.Broker);
			building.ObjectType = Facilities.KnownObjects.BuildingObjectType;
			building.AssetNo = DateTime.Now.Ticks.ToString();
			building.Name = DateTime.Now.Ticks.ToString();

			return building;
		}

		public static void CreateRelation(ISession session, Facilities.FacilityObject left, Facilities.FacilityObject right, Bmm.Obj.MaintenableObjectRelationType relType)
		{
			if (left == null || right == null)
				return;

			Bmm.Obj.MaintenableObjectRelation rel = Bmm.Obj.MaintenableObjectRelation.Create(session.Broker);
			rel.Left = left;
			rel.Right = right;
			rel.Type = relType;
		}

		/// <summary>
		/// Erstellt eine Assoziation zwischen dem übergebenen Vertrag und dem MaintenableObject
		/// </summary>
		/// <param name="session"></param>
		/// <param name="contract"></param>
		/// <param name="mo"></param>
		/// <returns></returns>
		public static Contracts.ContractAssociation CreateContractAssoc(ISession session, Contracts.Contract contract, Bmm.Obj.MaintenableObject mo)
		{
			if (contract == null || mo == null)
				return null;

			Contracts.ContractAssociation assoc = Contracts.ContractAssociation.Create(session.Broker);
			assoc.Contract = contract;
			assoc.MaintenableObject = mo;

			return assoc;
		}

		/// <summary>
		/// Erstellt eine Person mit Namen
		/// </summary>
		/// <param name="session"></param>
		/// <returns></returns>
		public static dotNetBF.Modules.Org.Person CreatePerson(ISession session)
		{
			dotNetBF.Modules.Org.Person person = dotNetBF.Modules.Org.Person.Create(session.Broker);
			person.Name = DateTime.Now.Ticks.ToString();

			return person;
		}

		/// <summary>
		/// Erstellt ein Konto mit Nummer und Bezeichnung.
		/// Default: DateTime.Now.Ticks
		/// </summary>
		/// <param name="session"></param>
		/// <param name="no"></param>
		/// <param name="name"></param>
		/// <returns></returns>
		public static Account CreateAccount(ISession session, string no = null, string name = null)
		{
			Account account = Account.Create(session.Broker);
			account.No = no ?? Guid.NewGuid().ToString();
			account.Name = name ?? DateTime.Now.Ticks.ToString();

			return account;
		}

		/// <summary>
		/// Erstellt ein Konto mit Nummer und Bezeichnung.
		/// Default: DateTime.Now.Ticks
		/// </summary>
		/// <param name="session"></param>
		/// <param name="no"></param>
		/// <returns></returns>
		public static CostCenter CreateCostCenter(ISession session, string no = null)
		{
			CostCenter cc = CostCenter.Create(session.Broker);
			cc.No = no ?? Guid.NewGuid().ToString();

			return cc;
		}

		public static Accounting.BillingAccount CreateBillingAccount(ISession session)
		{
			Accounting.BillingAccount billingAccount = Accounting.BillingAccount.Create(session.Broker);
			billingAccount.Name = DateTime.Now.Ticks.ToString();
			billingAccount.Value = DateTime.Now.Second;
			billingAccount.TaxKind = Common.Core.PaymentService.FullTax;

			return billingAccount;
		}

		public static Charge CreateCharge(ISession session, string number = null, double value = 0, DateTime? date = null)
		{
			if (dotNetBF.Modules.Services.Security.SecurityService.CurrentTenant.Country == null)
				dotNetBF.Modules.Services.Security.SecurityService.CurrentTenant.Country = Country.QueryExtent(session.Broker, "Name == 'Deutschland'").First;

			Charge charge = Charge.Create(session.Broker);
			charge.ChargeType = Common.Core.PaymentService.IncomingChargeType;
			charge.InvoiceDate = date ?? DateTime.Today;
			charge.FinancialYear = dotNetBF.Modules.Services.Security.SecurityService.CurrentUser.FinancialYear;
			charge.FromDateConsumption = charge.FinancialYear.From;
			charge.InvoiceValue = value;
			charge.InvoiceNumber = number ?? DateTime.Now.Ticks.ToString();
			charge.PostingText = charge.InvoiceNumber;
			charge.Account = CreateAccount(session);
			charge.TaxValue = CreateTaxCatalog(session, charge.Country);
			charge.InvoicingPerson = CreatePerson(session);
			return charge;
		}

		public static TaxCatalog CreateTaxCatalog(ISession session, Country country)
		{
			TaxCatalog taxCatalog = TaxCatalog.QueryExtent(session.Broker, "Country == c && Kind == GeoMan.Common.Core.PaymentService.NoTax && ValidFrom == new DateTime(1900,1,1)", new QueryParameter("c", country)).First ??
									TaxCatalog.Create(session.Broker);
			taxCatalog.Country = country;
			taxCatalog.TaxValue = 0;
			taxCatalog.Kind = Common.Core.PaymentService.NoTax;
			taxCatalog.ValidFrom = new DateTime(1900, 1, 1);

			return taxCatalog;
		}

		/// <summary>
		/// Bereitet den Request samt Parametern vor,
		/// </summary>
		/// <param name="param"></param>
		/// <returns></returns>
		public static HttpContext PrepareRequest(Dictionary<string, string> param)
		{
			System.Threading.Thread.GetDomain().SetData(".appDomain", "localtestdomain");
			System.Threading.Thread.GetDomain().SetData(".appVPath", "/localtest");
			System.Threading.Thread.GetDomain().SetData(".appPath", Environment.CurrentDirectory);
			string[] strings = param != null
				? param.Select(x => x.Key + "=" + x.Value).ToArray()
				: new string[]
				  {
				  };
			string queryString = string.Join("&", strings);
			var httprequest = new HttpRequest(null, "http://localhost", queryString);
			httprequest.Browser = new HttpBrowserCapabilities();
			httprequest.Browser.Capabilities = new Dictionary<object, object>();
			httprequest.Browser.Capabilities["browser"] = "IE";
			var httpcontext = new HttpContext(httprequest, new HttpResponse(new StringWriter()));
			return HttpContext.Current = httpcontext;
		}

		public static void CreateGermanDefaultAddress(ISession session)
		{
			Country cd = Country.QueryExtent(session.Broker, "Name like 'Deutschland'").First;
			if (cd == null)
			{
				cd = Country.Create(session.Broker);
				cd.Name = "Deutschland";
			}
			cd.IntlCode = "DEU";
			cd.IsActiveConf = false;
			Bmm.Geo.State ld = Bmm.Geo.State.QueryExtent(session.Broker, "Name like 'Sachsen' && Country.Name like 'Deutschland'").First;
			if (ld == null)
			{
				ld = Bmm.Geo.State.Create(session.Broker);
				ld.Name = "Sachsen";
				ld.Country = cd;
			}
			ld.Number = "14";
			ld.IsActiveConf = false;
			Region rd = Region.QueryExtent(session.Broker, "Name like 'Dresden' && State.Name like 'Sachsen' && State.Country.Name like 'Deutschland'").First;
			if (rd == null)
			{
				rd = Region.Create(session.Broker);
				rd.Name = "Dresden";
				rd.State = ld;
			}
			rd.RegionKey = "612000";
			LocalSubdistrict sd = LocalSubdistrict.QueryExtent(session.Broker, "Name like 'Striesen' && Region.Name like 'Dresden' && Region.State.Name like 'Sachsen' && Region.State.Country.Name like 'Deutschland'").First;
			if (sd == null)
			{
				sd = LocalSubdistrict.Create(session.Broker);
				sd.Name = "Striesen";
				sd.Region = rd;
			}
			sd.Key = "0260";
		}

        public static GeoMan.Tree.TreeType GetDefaultTreeType(ISession session)
        {
            var defaultTreeType = GeoMan.Tree.TreeType.QueryExtent(session.Broker, "NameLat==param0", new QueryParameter("param0", "--UnitTest--")).First;
            if (defaultTreeType == null)
            {
                defaultTreeType = GeoMan.Tree.TreeType.Create(session.Broker);
                defaultTreeType.NameLat = "--UnitTest--";
            }
            return defaultTreeType;
        }

        //Methode zum Erstellen eins Exchange Servers
        public static GroupWare.Server CreateExchangeServer(ISession session)
        {
            GroupWare.Server server = GroupWare.Server.Create(session.Broker);
            server.Domain = "kms-computer.de";
			//s.Port = 25;                      // bei SMTP durch Event gesetzt
			//s.Type = "Microsoft Exchange";    // durch Event gesetzt
            server.UserEmail = "testuser2@kms-computer.de";
            server.UserName = "testuser2";
            server.UserPwd = "gieKAw9ioGH5IzLFRKk3Vg==";
            server.Version = 4;
            return server;
        }

        //Methode zum Erstellen eines Exchange Service (Exchange Server nötig)
        public static ExchangeService CreateExchangeService(GroupWare.Server server)
        {
            ExchangeService service = new ExchangeService((ExchangeVersion)server.Version);
            service.Credentials = new WebCredentials(server.UserName, GeoMan.Common.Core.Crypto.DecryptString(server.UserPwd), server.Domain);
            service.AutodiscoverUrl(server.UserEmail);
            return service;
        }

        //Methode zum Versenden einer E-Mail mittels Exchange Service (Exchnage Service nötig)
        public static void SendMail(ExchangeService service)
        {
            EmailMessage message = new EmailMessage(service);
            message.Subject = "Testmeldung";
            message.Body = "Dies ist eine Testmeldung";
            message.ToRecipients.Add("testuser2@kms-computer.de");
            //message.Attachments.AddFileAttachment()               //Anhang anfügen
            message.SendAndSaveCopy();
        }
    }
}