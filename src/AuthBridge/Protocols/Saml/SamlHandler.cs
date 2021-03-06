using System;
using System.Collections.Generic;
using System.IdentityModel.Metadata;
using System.IdentityModel.Tokens;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.ServiceModel.Security;
using System.Text;
using System.Web;
using System.Xml;
using AuthBridge.Model;
using AuthBridge.Utilities;
using log4net;
using ClaimTypes = System.IdentityModel.Claims.ClaimTypes;

namespace AuthBridge.Protocols.Saml
{
	public class SamlHandler : ProtocolHandlerBase
	{
		private string[] _signingKeyThumbprint;
		private readonly string _issuer;
		private string _identityProviderSSOURL;
		private readonly string _audienceRestriction;
		private readonly string _requestedAuthnContextComparisonMethod;
		private readonly List<string> _authnContextClassRefs;
		private static readonly ILog Logger = LogManager.GetLogger(typeof(SamlHandler));
		
		public SamlHandler(ClaimProvider issuer)
			: base(issuer)
		{
			_issuer = string.IsNullOrEmpty(issuer.Parameters["issuer"]) ? MultiProtocolIssuer.Identifier.ToString() : issuer.Parameters["issuer"];
			if (!string.IsNullOrEmpty(issuer.Parameters["metadataUrl"]))
			{
				ParseMetadata(issuer);
			}
			else
			{
				_signingKeyThumbprint = new[] {issuer.Parameters["signingKeyThumbprint"].ToLowerInvariant()};
				_identityProviderSSOURL = issuer.Parameters["identityProviderSSOURL"];
			}
			_audienceRestriction = issuer.Parameters["audienceRestriction"];
			_requestedAuthnContextComparisonMethod = issuer.Parameters["requestedAuthnContextComparisonMethod"];
			var authnContextClassRefs = issuer.Parameters["authnContextClassRefs"];
			_authnContextClassRefs = !string.IsNullOrWhiteSpace(authnContextClassRefs)
				? authnContextClassRefs.Split(',').ToList()
				: new List<string>();
		}

		private void ParseMetadata(ClaimProvider issuer)
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
			var serializer = new MetadataSerializer {CertificateValidationMode = X509CertificateValidationMode.None};
			if ("true".Equals(issuer.Parameters["ignoreSslError"], StringComparison.InvariantCultureIgnoreCase))
			{
				ServicePointManager.ServerCertificateValidationCallback += (s, ce, ch, ssl) => true;
			}
			var metadata = serializer.ReadMetadata(XmlReader.Create(issuer.Parameters["metadataUrl"]));
			var entityDescriptor = (EntityDescriptor) metadata;
			
			var ssod = entityDescriptor.RoleDescriptors.OfType<IdentityProviderSingleSignOnDescriptor>().First();
			if (ssod == null)
			{
				throw new InvalidOperationException("Missing IdentityProviderSingleSignOnDescriptor!");
			}
			Logger.Info("Got IdentityProviderSingleSignOnDescriptor from metadata.");
			_identityProviderSSOURL =
				ssod.SingleSignOnServices.Single(
					x => x.Binding.ToString() == "urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect").Location.ToString();
			Logger.Info($"_identityProviderSSOURL: {_identityProviderSSOURL}");
			_signingKeyThumbprint = GetSigningKeyThumbprint(ssod).ToArray();
			if(Logger.IsInfoEnabled)
				Logger.Info($"signing key thumbprints: {string.Join(", ", _signingKeyThumbprint)}");
		}

		private static IEnumerable<string> GetSigningKeyThumbprint(RoleDescriptor ssod)
		{
			var x509DataClauses = ssod.Keys.Where(key => key.KeyInfo != null && key.Use == KeyType.Signing)
				.Select(key => key.KeyInfo.OfType<X509RawDataKeyIdentifierClause>().First());
			var tokens = new List<X509SecurityToken>();
			tokens.AddRange(x509DataClauses.Select(token => new X509SecurityToken(new X509Certificate2(token.GetX509RawData()))));
			Logger.Info($"Get signing keys: {tokens.Count}");
			return tokens.Select(x => x.Certificate.Thumbprint.ToLowerInvariant());
		}

		public override void ProcessSignInRequest(Scope scope, HttpContextBase httpContext)
		{
			var samlRequest = new AuthRequest(MultiProtocolIssuer.ReplyUrl.ToString(), _issuer, _audienceRestriction, _requestedAuthnContextComparisonMethod, _authnContextClassRefs);
			var preparedRequest = samlRequest.GetRequest(AuthRequest.AuthRequestFormat.Base64 | AuthRequest.AuthRequestFormat.Compressed | AuthRequest.AuthRequestFormat.UrlEncode);
			var returnUrl = GetReturnUrlQueryParameterFromUrl(httpContext.Request.UrlConsideringLoadBalancerHeaders().AbsoluteUri);
			var redirectUrl = _identityProviderSSOURL.Contains("?")
				? $"{_identityProviderSSOURL}&SAMLRequest={preparedRequest}&RelayState={returnUrl}"
				: $"{_identityProviderSSOURL}?SAMLRequest={preparedRequest}&RelayState={returnUrl}";

			try
			{
				httpContext.Response.Redirect(redirectUrl);
				httpContext.Response.End();
			}
			catch (Exception ex) when (HttpContext.Current.Response.HeadersWritten)
			{
				Logger.Error("exception while redirect to provider", ex);
			}
		}
		
		public override ClaimsIdentity ProcessSignInResponse(string realm, string originalUrl, HttpContextBase httpContext)
		{
			Logger.Info("ProcessSignInResponse");
			var response = Encoding.UTF8.GetString(Convert.FromBase64String(httpContext.Request.Form["SAMLResponse"]));
			Logger.InfoFormat("SAMLResponse: {0}", response);
			var doc = new XmlDocument();
			doc.LoadXml(response);
			VerifySignatures(doc);
			Logger.Info("Verified signature successfully");

			if (!VerifyStatus(doc))
			{
				ThrowAndLogWarn("The SAML response status was not 'status:Success'");
			}
			Logger.Info("Verified status successfully");

			var information = ExtractInformation(doc);
			Logger.InfoFormat("Extracted information: SubjectNameId: {0}, Issuer: {1}, NotBefore: {2}, NotOnOrAfter: {3}", information.SubjectNameId, information.Issuer, information.NotBefore, information.NotOnOrAfter);
			
			if (!VerifyAudience(information))
			{
				ThrowAndLog("Audience does not match the white list values.");
			}
			Logger.Info("Verified audience successfully");

			if (!VerifyAllowedDateTimeRange(information))
			{
				ThrowAndLogWarn("This SAML response is not valid any longer.");
			}
			Logger.Info("Verified allowed date time range successfully");

			Logger.InfoFormat("information.Issuer: {0}, information.SubjectNameId: {1}", information.Issuer, information.SubjectNameId);
			//You must add a claims policy for the protocol identifier!
			var issuerIdentifier = information.Issuer;
			var claims = new List<Claim>
			{
				new Claim(ClaimTypes.NameIdentifier, information.SubjectNameId)
			};
			return new ClaimsIdentity(claims, issuerIdentifier);
		}

		private static void ThrowAndLogWarn(string message)
		{
			Logger.Warn(message);
			throw new InvalidOperationException(message);
		}

		private static void ThrowAndLog(string message)
		{
			Logger.Error(message);
			throw new InvalidOperationException(message);
		}

		private static string GetReturnUrlQueryParameterFromUrl(string context)
		{
			var queryNameValueCollection = HttpUtility.ParseQueryString(context);

			var returnUrl = queryNameValueCollection["wctx"];
			if (!string.IsNullOrEmpty(returnUrl))
			{
				returnUrl = returnUrl.Replace("ru=", "");
				if (!returnUrl.EndsWith("/"))
					returnUrl += "/";
			}
			return returnUrl;
		}

		private static bool VerifyAllowedDateTimeRange(SamlDetail detail)
		{
			var utcnow = DateTime.UtcNow.TruncateToSecond();
			var notBefore = detail.NotBefore.TruncateToSecond();
			var notOnOrAfter = detail.NotOnOrAfter.TruncateToSecond();
			var notBeforeSubtract5Second = notBefore.Subtract(TimeSpan.FromSeconds(5));
			Logger.InfoFormat($"utcnow: {utcnow}, notBefore: {notBefore}, notOnOrAfter: {notOnOrAfter}, notBeforeSubtract5Second <= utcnow: {notBeforeSubtract5Second <= utcnow}, utcnow < notOnOrAfter: {utcnow < notOnOrAfter}");
			return notBeforeSubtract5Second <= utcnow && utcnow < notOnOrAfter;
		}

		private static SamlDetail ExtractInformation(XmlDocument doc)
		{
			var detail = new SamlDetail();
			var conditionsElement = doc.SelectSingleNode("//*[local-name()='Conditions']");
			if (conditionsElement != null)
			{
				detail.NotBefore = XmlConvert.ToDateTime(conditionsElement.Attributes["NotBefore"].Value, XmlDateTimeSerializationMode.Utc);
				detail.NotOnOrAfter = XmlConvert.ToDateTime(conditionsElement.Attributes["NotOnOrAfter"].Value, XmlDateTimeSerializationMode.Utc);
			}

			var nameIdElement = doc.SelectSingleNode("//*[local-name()='Subject']/*[local-name()='NameID']");
			if (nameIdElement == null)
			{
				ThrowAndLog("NameID Claim Policy not configured correctly.");
			}
			detail.SubjectNameId = nameIdElement.InnerText;

			var issuerElement = doc.SelectSingleNode("//*[local-name()='Issuer']");
			detail.Issuer = issuerElement.InnerText;
			var audienceElements = doc.SelectNodes("//*[local-name()='Conditions']/*[local-name()='AudienceRestriction']/*[local-name()='Audience']");
			detail.AudienceRestrictions = new List<string>();
			if (audienceElements != null)
			{
				foreach (var audienceElement in audienceElements)
					detail.AudienceRestrictions.Add(((XmlNode)audienceElement).InnerText);
			}
			return detail;
		}

		private void VerifySignatures(XmlDocument xmlDoc)
		{
			var isThumbprintCorrect = false;
			foreach (XmlElement item in xmlDoc.SelectNodes("//*[local-name()='Signature']"))
			{
				var node = item;
				if (node.ParentNode != xmlDoc.DocumentElement)
				{
					var doc = new XmlDocument();
					var parentNode = doc.ImportNode(item.ParentNode, true);
					doc.AppendChild(parentNode);
					node = (XmlElement)parentNode.SelectSingleNode("*[local-name()='Signature']");
				}
				var signedXml = new SignedXml((XmlElement)node.ParentNode);
				signedXml.LoadXml(node);
				if (!signedXml.CheckSignature())
				{
					ThrowAndLogWarn($"Verify {node.ParentNode.Name} signature failed.");
					return;
				}

				var x509Data = signedXml.Signature.KeyInfo.OfType<KeyInfoX509Data>().First();
				var cert = x509Data.Certificates.OfType<X509Certificate2>().First();
				if (cert.Thumbprint != null && Array.IndexOf(_signingKeyThumbprint, cert.Thumbprint.ToLowerInvariant()) > -1)
				{
					isThumbprintCorrect = true;
				}
			}

			if (!isThumbprintCorrect)
			{
				ThrowAndLog("The thumbprint doesn't match the white list values.");
			}
		}

		private static bool VerifyStatus(XmlDocument doc)
		{
			var statusCode = doc.SelectSingleNode("//*[local-name()='Status']/*[local-name()='StatusCode']");
			return statusCode.Attributes["Value"].Value.EndsWith("status:Success");
		}

		private bool VerifyAudience(SamlDetail information)
		{
			if (string.IsNullOrEmpty(_audienceRestriction))
				return true;

			return information.AudienceRestrictions.Contains(_audienceRestriction);
		}
	}
}