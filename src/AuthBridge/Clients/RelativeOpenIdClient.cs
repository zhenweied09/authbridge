using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Web;
using AuthBridge.Clients.Util;
using AuthBridge.Utilities;
using DotNetOpenAuth.OpenId;
using DotNetOpenAuth.OpenId.Extensions.AttributeExchange;
using DotNetOpenAuth.OpenId.RelyingParty;
using log4net;

namespace AuthBridge.Clients
{
	public class RelativeOpenIdClient : DotNetOpenAuth.AspNet.Clients.OpenIdClient
	{
		private static readonly ILog Logger = LogManager.GetLogger(typeof (RelativeOpenIdClient));
		private readonly Uri _realmUri;

		public RelativeOpenIdClient(Uri url, Uri realmUri)
			: base("Relative", url)
		{
			_realmUri = realmUri;
		}

		public override void RequestAuthentication(HttpContextBase context, Uri returnUrl)
		{
			var request = Guid.NewGuid();
			var relyingPartyField = typeof(DotNetOpenAuth.AspNet.Clients.OpenIdClient).GetField("RelyingParty",
				BindingFlags.Static | BindingFlags.NonPublic);
			var providerIdentifierField = typeof(DotNetOpenAuth.AspNet.Clients.OpenIdClient).GetField(
				"providerIdentifier", BindingFlags.NonPublic | BindingFlags.Instance);
			var relyingParty = (OpenIdRelyingParty)relyingPartyField.GetValue(this);
			var realm = new Realm(_realmUri ?? new Uri(returnUrl.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped)));
			var userSuppliedIdentifier = new Uri((Identifier)providerIdentifierField.GetValue(this));
			var localhost = new Uri(ConfigurationManager.AppSettings["CustomEndpointHost"] ?? "http://localhost/");
			var userSuppliedIdentifierForRequestMachine = new Uri(localhost,
				new Uri(userSuppliedIdentifier.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped)).MakeRelativeUri(userSuppliedIdentifier));

			Logger.InfoFormat("Request {0}; userSuppliedIdentifier {1}; userSuppliedIdentifierForRequestMachine {2}", request, userSuppliedIdentifier, userSuppliedIdentifierForRequestMachine);
			IAuthenticationRequest authenticationRequest = relyingParty.CreateRequest(userSuppliedIdentifierForRequestMachine, realm, returnUrl);
			OnBeforeSendingAuthenticationRequest(authenticationRequest);

			try
			{
				var property = authenticationRequest.DiscoveryResult.GetType().GetProperty("ProviderEndpoint");
				var providerEndPointUri = (Uri)property.GetValue(authenticationRequest.DiscoveryResult, null);

				var providerEndPointUriRequestMachine = new Uri(new Uri(context.Request.UrlConsideringLoadBalancerHeaders().GetComponents(UriComponents.SchemeAndServer,UriFormat.Unescaped)),
					new Uri(providerEndPointUri.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped)).MakeRelativeUri(providerEndPointUri));

				Logger.InfoFormat("Request {0}; userSupplied {1}", request, userSuppliedIdentifier);
				property.SetValue(authenticationRequest.DiscoveryResult, providerEndPointUriRequestMachine, BindingFlags.SetProperty, null, null, CultureInfo.CurrentCulture);
			}
			catch (Exception ex)
			{
				Logger.Error("Error in discovery modification", ex);
				throw;
			}

			try
			{
				authenticationRequest.RedirectToProvider();
			}
			catch (HttpException ex) when (ex.ErrorCode == -2147467259)
			{
				// ignore remote host closed the connection exception
				Logger.Warn("remote host closed the connection exception", ex);
			}
			catch (Exception ex) when (HttpContext.Current.Response.HeadersWritten)
			{
				Logger.Error("exception while redirect to provider", ex);
			}
		}

		protected override Dictionary<string, string> GetExtraData(IAuthenticationResponse response)
		{
			var fetchResponse = response.GetExtension<FetchResponse>();
			if (fetchResponse != null)
			{
				var extraData = new Dictionary<string, string>();
				extraData.AddItemIfNotEmpty(ClaimTypes.IsPersistent, fetchResponse.GetAttributeValue(ClaimTypes.IsPersistent));
				extraData.AddItemIfNotEmpty(ClaimTypes.AuthenticationMethod, fetchResponse.GetAttributeValue(ClaimTypes.AuthenticationMethod));

				return extraData;
			}
			return null;
		}
	}
}