﻿using Alphora.Dataphor.Dataphoria.Web.Core;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace Alphora.Dataphor.Dataphoria.Web.FHIR
{
	public class WebApiApplication : System.Web.HttpApplication
	{
		protected void Application_Start()
		{
			AreaRegistration.RegisterAllAreas();
			GlobalConfiguration.Configure(WebApiConfig.Register);
			FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
			RouteConfig.RegisterRoutes(RouteTable.Routes);
			BundleConfig.RegisterBundles(BundleTable.Bundles);

			ProcessorInstance.Initialize();
		}
		
		protected void Application_End()
		{
			ProcessorInstance.Uninitialize();
		}
	}
}
