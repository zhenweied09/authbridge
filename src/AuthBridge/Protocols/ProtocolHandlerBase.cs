﻿using System;
using System.Security.Claims;
using System.Web;
using AuthBridge.Configuration;
using AuthBridge.Model;
using Microsoft.Practices.Unity;

namespace AuthBridge.Protocols
{
	public abstract class ProtocolHandlerBase : IProtocolHandler
	{
		protected ProtocolHandlerBase(ClaimProvider issuer) : this(issuer, ServiceLocator.Container.Value.Resolve<IConfigurationRepository>())
		{
		}

		protected ProtocolHandlerBase(ClaimProvider issuer, IConfigurationRepository configuration)
		{
			if (issuer == null)
				throw new ArgumentNullException(nameof(issuer));

			if (configuration == null)
				throw new ArgumentNullException(nameof(configuration));

			Issuer = issuer;
			Configuration = configuration;
			MultiProtocolIssuer = Configuration.MultiProtocolIssuer;              
		}

		protected ClaimProvider Issuer { get; set; }

		protected IConfigurationRepository Configuration { get; set; }

		protected MultiProtocolIssuer MultiProtocolIssuer { get; set; }

		public abstract void ProcessSignInRequest(Scope scope, HttpContextBase httpContext);
		public abstract ClaimsIdentity ProcessSignInResponse(string realm, string originalUrl, HttpContextBase httpContext);
	}
}