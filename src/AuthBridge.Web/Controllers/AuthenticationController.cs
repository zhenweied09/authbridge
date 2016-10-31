﻿using System.Configuration;
using System.IdentityModel.Services;
using System.IdentityModel.Tokens;
using System.Linq;
using System.Security.Claims;
using System.Web;
using AuthBridge.Clients.Util;
using log4net;

namespace AuthBridge.Web.Controllers
{
    using System;
    using System.Globalization;
    using System.Security.Principal;
    using System.Web.Mvc;

    using Services;

    using Configuration;
    using Model;
    using SecurityTokenService;

	[HandleError]
    public class AuthenticationController : Controller
    {
	    private static readonly ILog Logger = LogManager.GetLogger(typeof (AuthenticationController));
        private readonly IProtocolDiscovery protocolDiscovery;

        private readonly IFederationContext federationContext;

        private readonly IConfigurationRepository configuration;

        private readonly MultiProtocolIssuer multiProtocolServiceProperties;

        public AuthenticationController()
			: this(new DefaultProtocolDiscovery(), new FederationContext(), DefaultConfigurationRepository.Instance)
        {
        }

        public AuthenticationController(IProtocolDiscovery defaultProtocolDiscovery, IFederationContext federationContext, IConfigurationRepository configuration)
        {
			protocolDiscovery = defaultProtocolDiscovery;
            this.federationContext = federationContext;
            this.configuration = configuration;
            multiProtocolServiceProperties = this.configuration.MultiProtocolIssuer;
        }

		public ActionResult HomeRealmDiscovery(string errorMessage = "")
		{
			Logger.Info("HomeRealmDiscovery!");
			var vms = configuration.RetrieveIssuers().Where(x => !x.IdpInitiatedOnly).Select(x => new ProviderViewModel
			{
				Identifier = x.Identifier.ToString(),
				DisplayName = x.DisplayName
			});
			return View("Authenticate", new HrdViewModel {Providers = vms.ToArray(), ErrorMessage = errorMessage });
		}

		public ActionResult Authenticate()
        {
			Logger.Info("Authenticate!");
            var identifier = new Uri(Request.QueryString[WSFederationConstants.Parameters.HomeRealm]);

            ClaimProvider issuer = configuration.RetrieveIssuer(identifier);
            if (issuer == null)
            {
                return HomeRealmDiscovery();
            }

            var handler = protocolDiscovery.RetrieveProtocolHandler(issuer);
            if (handler == null)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, "The protocol handler '{0}' was not found in the container", issuer.Protocol));
            }

			federationContext.IssuerName = issuer.Identifier.ToString();
	        if (string.IsNullOrEmpty(federationContext.Realm))
	        {
				throw new InvalidOperationException("The context cookie was not found. Try to sign in again.");
	        }
            var scope = configuration.RetrieveScope(new Uri(federationContext.Realm));
            if (scope == null)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, "The scope '{0}' was not found in the configuration", federationContext.Realm));
            }

            handler.ProcessSignInRequest(scope, HttpContext);
            
            return new EmptyResult();
        }

		private void ProcessResponse(string issuerName, string originalUrl)
		{
			var issuer = configuration.RetrieveIssuer(new Uri(issuerName));
			Logger.InfoFormat("ProcessResponse! issuer: {0}", issuer.DisplayName);

			var handler = protocolDiscovery.RetrieveProtocolHandler(issuer);
			Logger.InfoFormat("ProcessResponse! handler: {0}", handler);

			if (handler == null)
				throw new InvalidOperationException();

            ClaimsIdentity identity = handler.ProcessSignInResponse(
                                                                federationContext.Realm,
                                                                federationContext.OriginalUrl,
                                                                HttpContext);

	        var protocolIdentifier = multiProtocolServiceProperties.Identifier.ToString();
	        var issuerIdentifier = issuer.Identifier.ToString();
	        ClaimsIdentity outputIdentity = UpdateIssuer(identity, protocolIdentifier, issuerIdentifier);
            outputIdentity.AddClaim(new Claim(ClaimTypes.AuthenticationMethod, issuerIdentifier, ClaimValueTypes.String, protocolIdentifier));
            outputIdentity.AddClaim(new Claim(ClaimTypes.AuthenticationInstant, DateTime.UtcNow.ToString("o"), ClaimValueTypes.DateTime, protocolIdentifier));

			if (Logger.IsInfoEnabled)
			{
				foreach (var claim in outputIdentity.Claims)
				{
					Logger.InfoFormat("added claim, claim.Type: {0}, claim.Value: {1}, claim.ValueType: {2}, claim.Issuer: {3}",
						claim.Type, claim.Value, claim.ValueType, claim.Issuer);
				}
			}
			var sessionToken = new SessionSecurityToken(new ClaimsPrincipal(new[] { outputIdentity }), new TimeSpan(0, 30, 0));
			FederatedAuthentication.WSFederationAuthenticationModule.SetPrincipalAndWriteSessionToken(sessionToken, true);

			Logger.InfoFormat("Original url: {0}", originalUrl);
			Response.Redirect(originalUrl, false);
		}

		[ValidateInput(false)]
		public void ProcessResponse()
		{
			Logger.Info("ProcessResponse!");
			if (string.IsNullOrEmpty(federationContext.IssuerName))
			{
				Logger.ErrorFormat("The context cookie was not found. Try to sign in again.");
				throw new InvalidOperationException("The context cookie was not found. Try to sign in again.");
			}
			Logger.InfoFormat("ProcessResponse! federationContext.IssuerName: {0}", federationContext.IssuerName);
			Logger.InfoFormat("ProcessResponse! federationContext.OriginalUrl: {0}", federationContext.OriginalUrl);

			ProcessResponse(federationContext.IssuerName, federationContext.OriginalUrl);

			federationContext.Destroy();
			HttpContext.ApplicationInstance.CompleteRequest();
		}

		public void ProcessIdpInitiatedRequest(string protocol)
		{
			var protocolIdentifier = "urn:" + protocol;

			var scope = configuration.RetrieveDefaultScope();
			if (scope == null)
			{
				Response.Write(protocol + " IdP initiated failed.");
				Response.End();
				return;
			}
			var relayState = Request.Form["RelayState"];
			var returnUrl = string.IsNullOrWhiteSpace(relayState) ? "" : relayState;

			var originalUrl = string.Format("?wa=wsignin1.0&wtrealm={0}&wctx={1}&whr={2}", Uri.EscapeDataString(scope.Identifier), "ru=" + returnUrl, Uri.EscapeDataString(protocolIdentifier));
			ProcessResponse(protocolIdentifier, originalUrl);
			HttpContext.ApplicationInstance.CompleteRequest();
		}

	    public ActionResult ProcessFederationRequest()
        {
			Logger.Info("ProcessFederationRequest");
			var action = Request.QueryString[WSFederationConstants.Parameters.Action];

            try
            {
                switch (action)
                {
                    case WSFederationConstants.Actions.SignIn:
                        {
                            var requestMessage = (SignInRequestMessage)WSFederationMessage.CreateFromUri(Request.Url);
                            
							
                            if (User != null && User.Identity != null && User.Identity.IsAuthenticated)
                            {
                                var sts = new MultiProtocolSecurityTokenService(MultiProtocolSecurityTokenServiceConfiguration.Current);
	                            if (Logger.IsInfoEnabled)
	                            {
		                            var user = User.Identity as ClaimsIdentity;
		                            if (user != null && user.Claims != null)
		                            {
			                            foreach (var claim in user.Claims)
			                            {
				                            Logger.InfoFormat(
												"claim, Issuer: {0}, OriginalIssuer: {1}, Type:{2}, Subject:{3}, Value: {4}, ValueType: {5}",
					                            claim.Issuer, claim.OriginalIssuer, claim.Type, claim.Subject, claim.Value,
					                            claim.ValueType);
			                            }
		                            }
									Logger.InfoFormat("Reply: {0}",requestMessage.Reply);
	                            }
								Logger.InfoFormat("Before ProcessSignInRequest");
                                var responseMessage = FederatedPassiveSecurityTokenServiceOperations.ProcessSignInRequest(requestMessage, new ClaimsPrincipal(User), sts);
								FederatedAuthentication.SessionAuthenticationModule.DeleteSessionTokenCookie();
								responseMessage.Write(Response.Output);
                                Response.Flush();
                                Response.End();
                                HttpContext.ApplicationInstance.CompleteRequest();
                            }
                            else
                            {
                                // user not authenticated yet, look for whr, if not there go to HomeRealmDiscovery page
								Logger.InfoFormat("User is not authenticated yet, redirecting to given realm.");
                                CreateFederationContext();

                                if (string.IsNullOrEmpty(Request.QueryString[WSFederationConstants.Parameters.HomeRealm]))
                                {
									return RedirectToAction("HomeRealmDiscovery", new { errorMessage = HttpUtility.ParseQueryString(requestMessage.Context).Get("em")});
                                }
	                            return Authenticate();
                            }
                        }

                        break;
                    case WSFederationConstants.Actions.SignOut:
                        {
                            var requestMessage = (SignOutRequestMessage)WSFederationMessage.CreateFromUri(Request.Url);
							var replyTo = requestMessage.Reply;
							if (!string.IsNullOrEmpty(replyTo) && ConfigurationManager.AppSettings.GetBoolSetting("UseRelativeConfiguration"))
							{
								var uri = new Uri(replyTo);
								if (uri.IsAbsoluteUri)
								{
									replyTo = "/" + new Uri(uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped)).MakeRelativeUri(uri);
								}
							}
                            FederatedPassiveSecurityTokenServiceOperations.ProcessSignOutRequest(requestMessage, new ClaimsPrincipal(User), replyTo, HttpContext.ApplicationInstance.Response);
                        }

                        break;
                    default:
                        Response.AddHeader("X-XRDS-Location",new Uri(Request.Url,Response.ApplyAppPathModifier("~/xrds.aspx")).AbsoluteUri);
                        return new EmptyResult();
                }
            }
            catch (Exception exception)
            {
                throw new Exception("An unexpected error occurred when processing the request. See inner exception for details.", exception);
            }

            return null;
        }

        protected override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            if (filterContext.HttpContext.User.Identity is WindowsIdentity)
                throw new InvalidOperationException("Windows authentication is not supported.");
        }

        private static ClaimsIdentity UpdateIssuer(ClaimsIdentity input, string issuer, string originalIssuer)
        {
            ClaimsIdentity outputIdentity = new ClaimsIdentity(new Claim[] {}, input.AuthenticationType);
            foreach (var claim in input.Claims)
            {
	            Logger.InfoFormat("outputIdentity.Claims.Add {0},{1},{2},{3}, {4}", claim.Type, claim.Value, claim.ValueType, issuer, originalIssuer);
                outputIdentity.AddClaim(new Claim(claim.Type, claim.Value, claim.ValueType, issuer, originalIssuer));
            }

            return outputIdentity;
        }

        private void CreateFederationContext()
        {
            federationContext.OriginalUrl = HttpContext.Request.Url.PathAndQuery;
            federationContext.Realm = Request.QueryString[WSFederationConstants.Parameters.Realm];
            federationContext.IssuerName = Request.QueryString[WSFederationConstants.Parameters.HomeRealm];
            federationContext.Context = Request.QueryString[WSFederationConstants.Parameters.Context];
        }
    }

	public class HrdViewModel
	{
		public ProviderViewModel[] Providers { get; set; }
		public string ErrorMessage { get; set; }
	}
}
