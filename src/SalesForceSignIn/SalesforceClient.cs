﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using AuthBridge.Clients.Util;
using DotNetOpenAuth.AspNet.Clients;

namespace SalesForceSignIn
{
	/// <summary>
	/// The Microsoft account client.
	/// </summary>
	public class SalesforceClient : OAuth2Client {
		#region Constants and Fields

		/// <summary>
		/// The authorization endpoint.
		/// </summary>
		private const string AuthorizationEndpoint = "https://login.salesforce.com/services/oauth2/authorize";

		/// <summary>
		/// The token endpoint.
		/// </summary>
		private const string TokenEndpoint = "https://login.salesforce.com/services/oauth2/token";

		/// <summary>
		/// The _app id.
		/// </summary>
		private readonly string appId;

		/// <summary>
		/// The _app secret.
		/// </summary>
		private readonly string appSecret;

		/// <summary>
		/// The requested scopes.
		/// </summary>
		private readonly string[] requestedScopes;

		private string idUrl;

		#endregion

		#region Constructors and Destructors

		/// <summary>
		/// Initializes a new instance of the <see cref="MicrosoftClient"/> class.
		/// Requests a scope of "wl.basic" by default, but "wl.signin" is a good minimal alternative.
		/// </summary>
		/// <param name="appId">The app id.</param>
		/// <param name="appSecret">The app secret.</param>
		public SalesforceClient(string appId, string appSecret)
			: this(appId, appSecret, "wl.basic")
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="MicrosoftClient"/> class.
		/// </summary>
		/// <param name="appId">The app id.</param>
		/// <param name="appSecret">The app secret.</param>
		/// <param name="requestedScopes">One or more requested scopes.</param>
		public SalesforceClient(string appId, string appSecret, params string[] requestedScopes)
			: this("salesforce", appId, appSecret, requestedScopes) {
			}

		/// <summary>
		/// Initializes a new instance of the <see cref="MicrosoftClient"/> class.
		/// </summary>
		/// <param name="providerName">The provider name.</param>
		/// <param name="appId">The app id.</param>
		/// <param name="appSecret">The app secret.</param>
		/// <param name="requestedScopes">One or more requested scopes.</param>
		protected SalesforceClient(string providerName, string appId, string appSecret, string[] requestedScopes)
			: base(providerName) {

			this.appId = appId;
			this.appSecret = appSecret;
			this.requestedScopes = requestedScopes;
			}

		#endregion

		/// <summary>
		/// Gets the identifier for this client as it is registered with Microsoft.
		/// </summary>
		protected string AppId {
			get { return this.appId; }
		}

		#region Methods

		/// <summary>
		/// Gets the full url pointing to the login page for this client. The url should include the specified return url so that when the login completes, user is redirected back to that url.
		/// </summary>
		/// <param name="returnUrl">The return URL.</param>
		/// <returns>
		/// An absolute URL.
		/// </returns>
		protected override Uri GetServiceLoginUrl(Uri returnUrl) {
			var builder = new UriBuilder(AuthorizationEndpoint);

			builder.AppendQueryArgs(
				new Dictionary<string, string>
					{
						{"grant_type", "authorization_code"},
						{"client_id", this.appId},
						{"client_secret", this.appSecret},
						//{ "scope", string.Join(" ", this.requestedScopes) },
						{"response_type", "code"},
						{"redirect_uri", returnUrl.AbsoluteUri},
					});

			return builder.Uri;
		}

		/// <summary>
		/// Given the access token, gets the logged-in user's data. The returned dictionary must include two keys 'id', and 'username'.
		/// </summary>
		/// <param name="accessToken">
		/// The access token of the current user. 
		/// </param>
		/// <returns>
		/// A dictionary contains key-value pairs of user data 
		/// </returns>
		protected override IDictionary<string, string> GetUserData(string accessToken) {
			SalesForceSignIn.SalesforceClientUserData graph;
			var request = WebRequest.Create(idUrl);
			request.Headers.Add("Authorization", "Bearer " + accessToken);
			using (var response = request.GetResponse())
			{
				using (var responseStream = response.GetResponseStream())
				{
					graph = JsonHelper.Deserialize<SalesForceSignIn.SalesforceClientUserData>(responseStream);
				}
			}

			var userData = new Dictionary<string, string>();
			userData.AddItemIfNotEmpty("id", graph.Id);
			userData.AddItemIfNotEmpty("username", graph.Username);
			userData.AddItemIfNotEmpty("firstname", graph.FirstName);
			userData.AddItemIfNotEmpty("lastname", graph.LastName);
			return userData;
		}

		/// <summary>
		/// Queries the access token from the specified authorization code.
		/// </summary>
		/// <param name="returnUrl">
		/// The return URL. 
		/// </param>
		/// <param name="authorizationCode">
		/// The authorization code. 
		/// </param>
		/// <returns>
		/// The query access token.
		/// </returns>
		protected override string QueryAccessToken(Uri returnUrl, string authorizationCode) {
			var entity =
				MessagingUtilities.CreateQueryString(
					new Dictionary<string, string> {
						{ "client_id", this.appId },
						{ "redirect_uri", returnUrl.AbsoluteUri },
						{ "client_secret", this.appSecret },
						{ "code", authorizationCode },
						{ "grant_type", "authorization_code" },
					});

			WebRequest tokenRequest = WebRequest.Create(TokenEndpoint);
			tokenRequest.ContentType = "application/x-www-form-urlencoded";
			tokenRequest.ContentLength = entity.Length;
			tokenRequest.Method = "POST";

			using (Stream requestStream = tokenRequest.GetRequestStream()) {
				var writer = new StreamWriter(requestStream);
				writer.Write(entity);
				writer.Flush();
			}

			var tokenResponse = (HttpWebResponse)tokenRequest.GetResponse();
			if (tokenResponse.StatusCode == HttpStatusCode.OK) {
				using (Stream responseStream = tokenResponse.GetResponseStream()) {
					var tokenData = JsonHelper.Deserialize<SalesForceSignIn.SalesForceOAuth2AccessTokenData>(responseStream);
					if (tokenData != null)
					{
						idUrl = tokenData.id;
						return tokenData.AccessToken;
					}
				}
			}

			return null;
		}

		#endregion
	}
}