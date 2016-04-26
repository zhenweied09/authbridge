﻿using System.Configuration;
using System.Linq;
using AuthBridge.Clients.Util;
using log4net;

namespace AuthBridge.Web.Controllers
{
    using System;
    using System.Globalization;
    using System.Security.Principal;
    using System.Web.Mvc;

    using Microsoft.IdentityModel.Claims;
    using Microsoft.IdentityModel.Protocols.WSFederation;
    using Microsoft.IdentityModel.Tokens;
    using Microsoft.IdentityModel.Web;

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

        public ActionResult HomeRealmDiscovery()
        {
			Logger.Info(string.Format("HomeRealmDiscovery!"));
			var vms = configuration.RetrieveIssuers().Where(x=>!x.IdpInitiatedOnly).Select(x => new ProviderViewModel
	        {
		        Identifier = x.Identifier.ToString(),
		        DisplayName = x.DisplayName
	        });
	        return View("Authenticate", vms.ToArray());
        }
        
        public ActionResult Authenticate()
        {
			Logger.Info(string.Format("Authenticate!"));
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

        [ValidateInput(false)]
        public void ProcessResponse()
        {
			Logger.Info(string.Format("ProcessResponse!"));
            if (string.IsNullOrEmpty(federationContext.IssuerName))
            {
				throw new InvalidOperationException("The context cookie was not found. Try to sign in again.");
            }
			Logger.Info(string.Format("ProcessResponse! federationContext.IssuerName: {0}", federationContext.IssuerName));
            var issuer = configuration.RetrieveIssuer(new Uri(federationContext.IssuerName));
			Logger.Info(string.Format("ProcessResponse! issuer: {0}", issuer.DisplayName));

            var handler = protocolDiscovery.RetrieveProtocolHandler(issuer);
			Logger.Info(string.Format("ProcessResponse! handler: {0}", handler));

            if (handler == null)
                throw new InvalidOperationException();

            IClaimsIdentity identity = handler.ProcessSignInResponse(
                                                                federationContext.Realm,
                                                                federationContext.OriginalUrl,
                                                                HttpContext);

	        var protocolIdentifier = multiProtocolServiceProperties.Identifier.ToString();
	        var issuerIdentifier = issuer.Identifier.ToString();
	        IClaimsIdentity outputIdentity = UpdateIssuer(identity, protocolIdentifier, issuerIdentifier);
            outputIdentity.Claims.Add(new Claim(ClaimTypes.AuthenticationMethod, issuerIdentifier, ClaimValueTypes.String, protocolIdentifier));
            outputIdentity.Claims.Add(new Claim(ClaimTypes.AuthenticationInstant, DateTime.Now.ToString("o"), ClaimValueTypes.Datetime, protocolIdentifier));


	        var sessionToken = new SessionSecurityToken(new ClaimsPrincipal(new[] {outputIdentity}), new TimeSpan(0, 30, 0));
            FederatedAuthentication.WSFederationAuthenticationModule.SetPrincipalAndWriteSessionToken(sessionToken, true);

            var originalUrl = federationContext.OriginalUrl;
			Logger.InfoFormat("Original url: {0}", originalUrl);
			Response.Redirect(originalUrl, false);
            federationContext.Destroy();
            HttpContext.ApplicationInstance.CompleteRequest();
        }

		public void ProcessIdpInitiatedRequest(string protocol)
		{
			var protocolIdentifier = "urn:" + protocol;
		    var issuer = configuration.RetrieveIssuer(new Uri(protocolIdentifier));
			var handler = protocolDiscovery.RetrieveProtocolHandler(issuer);
			var scope = configuration.RetrieveDefaultScope();
			if (scope == null)
			{
				Response.Write(protocol + " IdP initiated failed.");
				Response.End();
				return;
			}
			var relayState = Request.Form["RelayState"];
			var returnUrl = string.IsNullOrWhiteSpace(relayState) ? "~/Mytime" : relayState;
			var claimsIdentity = handler.ProcessSignInResponse(scope.Identifier, returnUrl, HttpContext);

			var identity = UpdateIssuer(claimsIdentity, claimsIdentity.AuthenticationType, protocolIdentifier);

			identity.Claims.Add(new Claim(ClaimTypes.AuthenticationMethod, claimsIdentity.AuthenticationType, ClaimValueTypes.String, protocolIdentifier));
			Logger.InfoFormat("added claim ClaimTypes.AuthenticationMethod# claimsIdentity.AuthenticationType: {0}, protocolIdentifier: {1}", claimsIdentity.AuthenticationType, protocolIdentifier);
			var dateTimeNow = DateTime.Now.ToString("o");
			identity.Claims.Add(new Claim(ClaimTypes.AuthenticationInstant, dateTimeNow, ClaimValueTypes.Datetime, protocolIdentifier));
			Logger.InfoFormat("added claim ClaimTypes.AuthenticationInstant# claimsIdentity.AuthenticationType: {0}, protocolIdentifier: {1}", dateTimeNow, protocolIdentifier);

			var sessionToken = new SessionSecurityToken(new ClaimsPrincipal(new[] {identity}));
		    FederatedAuthentication.WSFederationAuthenticationModule.SetPrincipalAndWriteSessionToken(sessionToken, true);
			
			Response.Redirect(string.Format("?wa=wsignin1.0&wtrealm={0}&wctx={1}&whr={2}", Uri.EscapeDataString(scope.Identifier), "ru="+ returnUrl, Uri.EscapeDataString(protocolIdentifier)), true);
		    Response.End();
	    }

	    public ActionResult ProcessFederationRequest()
        {
			Logger.Info(string.Format("ProcessFederationRequest"));

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
	                            if (((ClaimsIdentity) User.Identity) != null&& ((ClaimsIdentity)User.Identity).Claims != null)
	                            {
									foreach (var claim in ((ClaimsIdentity)User.Identity).Claims)
									{
										Logger.InfoFormat("claim, Issuer: {0}, OriginalIssuer: {1}, ClaimType:{2}, Subject:{3}, Value: {4}, ValueType: {5}", claim.Issuer, claim.OriginalIssuer, claim.ClaimType, claim.Subject, claim.Value, claim.ValueType);
									}
								}
                                var responseMessage = FederatedPassiveSecurityTokenServiceOperations.ProcessSignInRequest(requestMessage, User, sts);
                                responseMessage.Write(Response.Output);
                                Response.Flush();
                                Response.End();
                                HttpContext.ApplicationInstance.CompleteRequest();
                            }
                            else
                            {
                                // user not authenticated yet, look for whr, if not there go to HomeRealmDiscovery page
                                CreateFederationContext();

                                if (string.IsNullOrEmpty(Request.QueryString[WSFederationConstants.Parameters.HomeRealm]))
                                {
                                    return RedirectToAction("HomeRealmDiscovery");
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
                            FederatedPassiveSecurityTokenServiceOperations.ProcessSignOutRequest(requestMessage, User, replyTo, HttpContext.ApplicationInstance.Response);
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

        private static IClaimsIdentity UpdateIssuer(IClaimsIdentity input, string issuer, string originalIssuer)
        {
            IClaimsIdentity outputIdentity = new ClaimsIdentity();
            foreach (var claim in input.Claims)
            {
	            Logger.InfoFormat("outputIdentity.Claims.Add {0},{1},{2},{3}, {4}", claim.ClaimType, claim.Value, claim.ValueType, issuer, originalIssuer);
				outputIdentity.Claims.Add(new Claim(claim.ClaimType, claim.Value, claim.ValueType, issuer, originalIssuer));
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
}
