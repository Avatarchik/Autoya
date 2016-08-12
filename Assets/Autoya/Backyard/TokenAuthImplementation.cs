using UnityEngine;
using Connections.HTTP;
using System;
using System.Collections.Generic;
using UniRx;

namespace AutoyaFramework {
	public partial class Autoya {
		/*
			authentication implementation.
				this feature controls the flow of authentication.


			2 data required for authentication.
				identity,
				token


			identity:
				data which will be stored in app and use for identification between client to server.
				initially this parameter is empty.				
				client request this parameter to server -> server generates identity -> client get it and store it.

				deleting identity is the way of user-delete.


			token:
				data which will be temporaly stored in app and use for authorization between client to server.
				initially this parameter is empty and will be expired by server.

				client request this parameter to server with identity -> server generates token -> client get it and store it.
				
				deleting identity is the way of logout.
				LogIn phase can request some kind of authentication flow when user want to re-login with another accounts.


			Authentination in Autoya is based on the scenerio: "store identity, store token then use it for login".
				on Initial Running:
					client(vacant-identitiy, vacant-token) -> server
						get identity from server.
						then store identitiy into client.

					client(identitiy, vacant-token) -> server
						get first token from server.
						then store token into client.

				on LogIn:
					client(identitiy + token) -> server
						at login with id + token.

				on LoggedIn:(same with LogIn)
					client(identitiy + token) -> server
						at various connection.

				on LogOut:
					delete token from client.
						client(identitiy + token) becomes client(identitiy, vacant-token)
						LogIn phase can request some kind of authentication flow when user want to re-login with another accounts.

						
				on Erase:("App Erase" or "App Data Erase")
					client(identitiy + token) becomes client(vacant-identitiy, vacant-token)

				

			There are 3 way flow to gettting login.
				Initial Boot:
					1.get id and token
					2.login with token

				or 

				2nd Boot or later:
					1.load token from app
					2.login with token

				or 

				when token Expired:
					1.get token
					2.login with token
			
			
			Authorize-Extension
				Autoya's basic auth feature's identity parameter is generated by server on Initial Boot Time.
				It is useful because player does not requre anything for playing game.

				But if the player(App user) wanted to LogIn on their new device,
				We should provide the way to do it(CarryOut of identity).
				
				Autoya can enforce LogIn feature by additional user data.
				-> under construction.


			複数のハードポイントを吐き出せるはず。
			・idのsaveValidation
			・idのloadValidation

			・tokenのsaveValidation
			・tokenのloadValidation

			・tokenRequestのrequestHeaderValidation
			・tokenRequestのresponseHeaderValidation

			そも
		*/
		private string _identity;
		
		private string _token;
		private void InitializeTokenAuth () {
			_loginState = LoginState.LOGGED_OUT;

			/*
				set default handlers.
			*/
			OnLoginSucceeded = token => {
				LoggedIn(token);
			};

			OnAuthFailed = (conId, reason) => {
				LogOut();
				return false;
			};

			ReloadIdentity();
		}

		/**
			step 1 of 3.
			reload identity then load token then login.
		*/
		private void ReloadIdentity () {
			var identityCandidate = LoadIdentity();
			if (IsIdentityValid(identityCandidate)) {
				_identity = identityCandidate;
				Debug.LogWarning("ReloadIdentity succeeded.");
				LoadTokenThenLogin();
			} else {
				_identity = string.Empty;
				var saveResult = SaveIdentity(string.Empty);
				if (saveResult) {
					RequestIdentity();
				}
			}
		}

		private bool IsIdentityValid (string identityCandidate) {
			if (string.IsNullOrEmpty(identityCandidate)) return false; 
			return true;
		}

		private void RequestIdentity () {
			var identityUrl = AutoyaConsts.AUTH_URL_IDENTITY;
			var identityHttp = new HTTPConnection();
			var identityConnectionId = AutoyaConsts.AUTH_CONNECTIONID_REQUESTIDENTITY_PREFIX + Guid.NewGuid().ToString();
			
			Debug.LogWarning("内部にセットされているtokenとかを混ぜて、requestHeaderを生成する。");
			var identityRequestHeaders = new Dictionary<string, string>();

			Observable.FromCoroutine(
				() => identityHttp.Get(
					identityConnectionId,
					identityRequestHeaders,
					identityUrl,
					(conId, code, responseHeaders, data) => {
						EvaluateIdentityResult(conId, responseHeaders, code, data);
					},
					(conId, code, failedReason, responseHeaders) => {
						EvaluateIdentityResult(conId, responseHeaders, code, failedReason);
					}
				)
			).Timeout(
				TimeSpan.FromSeconds(AutoyaConsts.HTTP_TIMEOUT_SEC)
			).Subscribe(
				_ => {},
				ex => {
					var errorType = ex.GetType();

					switch (errorType.ToString()) {
						case AutoyaConsts.AUTH_HTTP_INTERNALERROR_TYPE_TIMEOUT: {
							EvaluateIdentityResult(identityConnectionId, new Dictionary<string, string>(), AutoyaConsts.AUTH_HTTP_INTERNALERROR_CODE_TIMEOUT, "timeout:" + ex.ToString());
							break;
						}
						default: {
							throw new Exception("failed to get token by undefined reason:" + ex.Message);
						}
					}
				}
			);
		}
		private void EvaluateIdentityResult (string identityConnectionId, Dictionary<string, string> responseHeaders, int responseCode, string resultDataOrFailedReason) {
			Debug.LogWarning("取得したidentityを検査する必要がある。ヘッダとかで検証とかいろいろ。 検証メソッドとか外に出せばいいことあるかな。");

			ErrorFlowHandling(
				identityConnectionId,
				responseHeaders, 
				responseCode,  
				resultDataOrFailedReason, 
				(succeededConId, succeededData) => {
					var isValid = IsIdentityValid(succeededData);
					
					if (isValid) {
						Debug.LogWarning("id取得に成功, 雑にidとして保存する。 succeededData:" + succeededData);

						var identitiyCandidate = succeededData;
						UpdateIdentityThenGetToken(identitiyCandidate);
					} else {
						Debug.LogError("未解決の、invalidなidentityだと見做せた場合の処理");
					}
				},
				(failedConId, failedCode, failedReason) => {
					if (IsInMaintenance(responseCode, responseHeaders)) {
						// in maintenance, do nothing here.
						return;
					}
				}
			);
		}

		private void UpdateIdentityThenGetToken (string newIdentity) {
			var isSaved = SaveIdentity(newIdentity);
			if (isSaved) {
				Debug.LogWarning("UpdateIdentityThenGetToken succeeded.");
				ReloadIdentity();
			} else {
				Debug.LogError("tokenのSaveに失敗、この辺、保存周りのイベントと連携させないとな〜〜〜");
			}
		}

		private int Progress () {
			return (int)_loginState;
		}

		private void LoggedIn (string newToken) {
			Debug.Assert(!(string.IsNullOrEmpty(newToken)), "token is null.");

			_token = newToken;
			_loginState = LoginState.LOGGED_IN;
		}

		private void LogOut () {
			_loginState = LoginState.LOGGED_OUT;
			RevokeToken();
		}

		/**
			step 2 of 3.
				load token then login.
		*/
		private void LoadTokenThenLogin () {
			Debug.Assert(!string.IsNullOrEmpty(_identity), "id is empty");
			var tokenCandidate = LoadToken();

			/*
				if token is already stored and valid, goes to login.
				else, get token then login.
			*/
			var isValid = IsTokenValid(tokenCandidate);
			
			if (isValid) {
				Debug.LogWarning("(maybe) valid token found. start login with it.");
				AttemptLoginByTokenCandidate(tokenCandidate);
			} else {
				Debug.LogWarning("no token found. get token then login.");
				GetTokenThenLogin();
			}
		}

		private bool SaveIdentity (string identity) {
			return _autoyaFilePersistence.Update(
				AutoyaConsts.AUTH_STORED_FRAMEWORK_DOMAIN, 
				AutoyaConsts.AUTH_STORED_IDENTITY_FILENAME,
				identity
			);
		}
		private string LoadIdentity () {
			return _autoyaFilePersistence.Load(
				AutoyaConsts.AUTH_STORED_FRAMEWORK_DOMAIN, 
				AutoyaConsts.AUTH_STORED_IDENTITY_FILENAME
			); 
		}

		private bool SaveToken (string newTokenCandidate) {
			return _autoyaFilePersistence.Update(
				AutoyaConsts.AUTH_STORED_FRAMEWORK_DOMAIN, 
				AutoyaConsts.AUTH_STORED_TOKEN_FILENAME,
				newTokenCandidate
			);
		}

		private string LoadToken () {
			return _autoyaFilePersistence.Load(
				AutoyaConsts.AUTH_STORED_FRAMEWORK_DOMAIN, 
				AutoyaConsts.AUTH_STORED_TOKEN_FILENAME
			);
		}

		private void GetTokenThenLogin () {
			Debug.Assert(!string.IsNullOrEmpty(_identity), "identity is null or empty.");
			_loginState = LoginState.GETTING_TOKEN;

			var tokenUrl = AutoyaConsts.AUTH_URL_TOKEN;
			var tokenHttp = new HTTPConnection();
			var tokenConnectionId = AutoyaConsts.AUTH_CONNECTIONID_GETTOKEN_PREFIX + Guid.NewGuid().ToString();
			
			Debug.LogWarning("内部に保存していたidentityをアレしたものを使って、リクエストを作り出す。");
			var tokenRequestHeaders = new Dictionary<string, string>{
				{"identity", _identity}
			};

			Observable.FromCoroutine(
				() => tokenHttp.Get(
					tokenConnectionId,
					tokenRequestHeaders,
					tokenUrl,
					(conId, code, responseHeaders, data) => {
						EvaluateTokenResult(conId, responseHeaders, code, data);
					},
					(conId, code, failedReason, responseHeaders) => {
						EvaluateTokenResult(conId, responseHeaders, code, failedReason);
					}
				)
			).Timeout(
				TimeSpan.FromSeconds(AutoyaConsts.HTTP_TIMEOUT_SEC)
			).Subscribe(
				_ => {},
				ex => {
					var errorType = ex.GetType();

					switch (errorType.ToString()) {
						case AutoyaConsts.AUTH_HTTP_INTERNALERROR_TYPE_TIMEOUT: {
							EvaluateTokenResult(tokenConnectionId, new Dictionary<string, string>(), AutoyaConsts.AUTH_HTTP_INTERNALERROR_CODE_TIMEOUT, "timeout:" + ex.ToString());
							break;
						}
						default: {
							throw new Exception("failed to get token by undefined reason:" + ex.Message);
						}
					}
				}
			);
		}

		private void EvaluateTokenResult (string tokenConnectionId, Dictionary<string, string> responseHeaders, int responseCode, string resultDataOrFailedReason) {
			// 取得したtokenを検査する必要がある。ヘッダとかで検証とかいろいろ。 検証メソッドとか外に出せばいいことあるかな。
			Debug.LogWarning("EvaluateTokenResult!! " + " tokenConnectionId:" + tokenConnectionId + " responseCode:" + responseCode + " resultDataOrFailedReason:" + resultDataOrFailedReason);

			ErrorFlowHandling(
				tokenConnectionId,
				responseHeaders, 
				responseCode,  
				resultDataOrFailedReason, 
				(succeededConId, succeededData) => {
					var isValid = IsTokenValid(succeededData);
					
					if (isValid) {
						Debug.LogWarning("token取得に成功 succeededData:" + succeededData);
						var tokenCandidate = succeededData;
						UpdateTokenThenAttemptLogin(tokenCandidate);
					} else {
						Debug.LogError("未解決の、invalidなtokenだと見做せた場合の処理");
					}
				},
				(failedConId, failedCode, failedReason) => {
					_loginState = LoginState.LOGGED_OUT;
					
					if (IsInMaintenance(responseCode, responseHeaders)) {
						// in maintenance, do nothing here.
						return;
					}

					if (IsAuthFailed(responseCode, responseHeaders)) {
						// get token url should not return unauthorized response. do nothing here.
						return;
					}

					// other errors. 
					var shouldRetry = OnAuthFailed(tokenConnectionId, resultDataOrFailedReason);
					if (shouldRetry) {
						Debug.LogError("なんかtoken取得からリトライすべきなんだけどちょっとまってな1");
						// GetTokenThenLogin();
					} 
				}
			);
		}

		private bool IsTokenValid (string tokenCandidate) {
			if (string.IsNullOrEmpty(tokenCandidate)) return false; 
			return true;
		}


		/**
			step 3 of 3.
				login with token candidate.
				this method constructs kind of "Autoya's popular auth-signed http request".
		*/
		private void AttemptLoginByTokenCandidate (string tokenCandidate) {
			Debug.Assert(!string.IsNullOrEmpty(_identity), "identity is still empty.");

			_loginState = LoginState.LOGGING_IN;

			/*
				set token candidate and identitiy to request header basement.
			*/
			SetHTTPAuthorizedPart(_identity, tokenCandidate);

			/*
				create authorized login request.
			*/
			var loginUrl = AutoyaConsts.AUTH_URL_LOGIN;			
			var loginHeaders = GetAuthorizedAndAdditionalHeaders();

			var loginHttp = new HTTPConnection();
			var loginConnectionId = AutoyaConsts.AUTH_CONNECTIONID_ATTEMPTLOGIN_PREFIX + Guid.NewGuid().ToString();
			
			Observable.FromCoroutine(
				_ => loginHttp.Get(
					loginConnectionId,
					loginHeaders,
					loginUrl,
					(conId, code, responseHeaders, data) => {
						EvaluateLoginResult(conId, responseHeaders, code, data);
					},
					(conId, code, failedReason, responseHeaders) => {
						EvaluateLoginResult(conId, responseHeaders, code, failedReason);
					}
				)
			).Timeout(
				TimeSpan.FromSeconds(AutoyaConsts.HTTP_TIMEOUT_SEC)
			).Subscribe(
				_ => {},
				ex => {
					var errorType = ex.GetType();

					switch (errorType.ToString()) {
						case AutoyaConsts.AUTH_HTTP_INTERNALERROR_TYPE_TIMEOUT: {
							EvaluateLoginResult(loginConnectionId, new Dictionary<string, string>(), AutoyaConsts.AUTH_HTTP_INTERNALERROR_CODE_TIMEOUT, "timeout:" + ex.ToString());
							break;
						}
						default: {
							throw new Exception("failed to get token by undefined reason:" + ex.Message);
						}
					}
				}
			);
		}

		private void EvaluateLoginResult (string loginConnectionId, Dictionary<string, string> responseHeaders, int responseCode, string resultDataOrFailedReason) {
			// ログイン時のresponseHeadersに入っている情報について、ここで判別前〜後に扱う必要がある。
			ErrorFlowHandling(
				loginConnectionId,
				responseHeaders, 
				responseCode,  
				resultDataOrFailedReason, 
				(succeededConId, succeededData) => {
					Debug.LogWarning("EvaluateLoginResult tokenを使ったログイン通信に成功。401チェックも突破。これでログイン動作が終わることになる。");
					
					// ここで、内部で使ってたcandidate = 保存されてるやつ を、_tokenにセットして良さげ。
					// なんかサーバからtokenのハッシュとか渡してきて、ここで照合すると良いかもっていう気が少しした。
					var savedToken = LoadToken();
					OnLoginSucceeded(savedToken);
				},
				(failedConId, failedCode, failedReason) => {
					// if Unauthorized, OnAuthFailed is already called.
					if (IsAuthFailed(responseCode, responseHeaders)) return;
			
					_loginState = LoginState.LOGGED_OUT;

					/*
						we should handling NOT 401(Unauthorized) result.
					*/

					// tokenはあったんだけど通信失敗とかで予定が狂ったケースか。
					// tokenはあるんで、エラーわけを細かくやって、なんともできなかったら再チャレンジっていうコースな気がする。
					
					var shouldRetry = OnAuthFailed(loginConnectionId, resultDataOrFailedReason);
					if (shouldRetry) {
						Debug.LogError("ログイン失敗、リトライすべきなんだけどちょっとまってな2");
						// LoadTokenThenLogin();
					}
				}
			);
		}
		
		private void UpdateTokenThenAttemptLogin (string gotNewToken) {
			var isSaved = SaveToken(gotNewToken);
			if (isSaved) {
				LoadTokenThenLogin();
			} else {
				Debug.LogError("tokenのSaveに失敗、この辺、保存周りのイベントと連携させないとな〜〜〜");
			}
		}
		private void RevokeToken () {
			_token = string.Empty;
			var isRevoked = SaveToken(string.Empty);
			if (!isRevoked) Debug.LogError("revokeに失敗、この辺、保存周りのイベントと連携させないとな〜〜〜");
		}


		/*
			public auth APIs
		*/

		public static int Auth_Progress () {
			return autoya.Progress();
		}

		public static void Auth_AttemptLogIn () {
			autoya.ReloadIdentity();
		}

		public static void Auth_SetOnLoginSucceeded (Action onAuthSucceeded) {
			autoya.OnLoginSucceeded = token => {
				autoya.LoggedIn(token);
				onAuthSucceeded();
			};
			
			// if already logged in, fire immediately.
			if (Auth_IsLoggedIn()) onAuthSucceeded();
        }

		public static void Auth_SetOnAuthFailed (Func<string, string, bool> onAuthFailed) {
            autoya.OnAuthFailed = (conId, reason) => {
				autoya.LogOut();
				return onAuthFailed(conId, reason);
			};
        }

		public static void Auth_Logout () {
			autoya.LogOut();
		}
		
		private Action<string> OnLoginSucceeded;

		/**
			this method will be called when Autoya encountered "auth failed".
			caused by: received 401 response by some reason will raise this method.

			1.server returns 401 as the result of login request.
			2.server returns 401 as the result of usual http connection.

			and this method DOES NOT FIRE when logout intentionally.
		*/
		private Func<string, string, bool> OnAuthFailed;


		
		public enum LoginState : int {
			LOGGED_OUT,
			GETTING_TOKEN,
			LOGGING_IN,
			LOGGED_IN,
		}

		private LoginState _loginState;

		public static bool Auth_IsLoggedIn () {
			if (string.IsNullOrEmpty(autoya._token)) return false;
			if (autoya._loginState != LoginState.LOGGED_IN) return false; 
			return true;
		}
		
		/*
			test methods.
		*/
		public static void Auth_Test_CreateAuthError () {
			/*
				generate fake response for generate fake accidential logout error.
			*/
			autoya.ErrorFlowHandling(
				"Auth_Test_AccidentialLogout_ConnectionId", 
				new Dictionary<string, string>(),
				401, 
				"Auth_Test_AccidentialLogout test error", 
				(conId, data) => {}, 
				(conId, code, reason) => {}
			);
		}
		
	}
}