using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;

/**
	purchase feature of Autoya.
	should be use as singleton.

	local validation ver. for iOS and Android.(not including macOS)
*/
namespace AutoyaFramework.Purchase {
	public class LocalPurchaseRouter : IStoreListener {
		[Serializable] public struct PurchaseFailed {
			public string ticketId;
			public string reason;
			public PurchaseFailed (string ticketId, string reason) {
				this.ticketId = ticketId;
				this.reason = reason;
			}
		}
		
		public enum RouterState {
			None,
			
			LoadingStore,
			FailedToLoadStore,
			

			PurchaseReady,

			
			GettingTransaction,
			Purchasing,
		}

		public enum PurchaseError {
			Offline,
			UnavailableProduct,
			AlreadyPurchasing,
			InvalidReceipt,

			/*
				Unty IAP initialize errors.
			*/
			UnityIAP_Initialize_AppNowKnown,
			UnityIAP_Initialize_NoProductsAvailable,
			UnityIAP_Initialize_PurchasingUnavailable,


			/*
				Unity IAP Purchase errors.
			*/
			UnityIAP_Purchase_PurchasingUnavailable,
			UnityIAP_Purchase_ExistingPurchasePending,
			UnityIAP_Purchase_ProductUnavailable,
			UnityIAP_Purchase_SignatureInvalid,
			UnityIAP_Purchase_UserCancelled,
			UnityIAP_Purchase_PaymentDeclined,
			UnityIAP_Purchase_Unknown,

			UnknownError
		}

		private RouterState routerState = RouterState.None;
		
		private readonly string storeKind;


		private readonly Action readyPurchase;
		private readonly Action<PurchaseError, string, AutoyaStatus> failedToReady;
        private readonly Action<string> purchasedInBackground;

		private readonly string storeId;

		private readonly ProductInfo[] verifiedProducts;

		/**
			constructor.
		 */
		public LocalPurchaseRouter (ProductInfo[] productInfos, Action onReadyPurchase, Action<PurchaseError, string, AutoyaStatus> onReadyFailed, Action<string> onPurchaseDoneInBackground) {
			this.storeId = Guid.NewGuid().ToString();
			
			this.verifiedProducts = productInfos;

            this.readyPurchase = onReadyPurchase;
            this.failedToReady = onReadyFailed;
            this.purchasedInBackground = onPurchaseDoneInBackground;

			/*
				set store kind by platform.
			*/
			#if UNITY_EDITOR
				this.storeKind = AppleAppStore.Name;
			#elif UNITY_IOS
				this.storeKind = AppleAppStore.Name;
			#elif UNITY_ANDROID
				this.storeKind = GooglePlay.Name;
			#endif

			routerState = RouterState.LoadingStore;

			var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
			
			foreach (var productInfo in productInfos) {
				builder.AddProduct(
					productInfo.productId, 
					ProductType.Consumable, new IDs{
						{productInfo.platformProductId, storeKind}
					}
				);
			}

			/*
				check network connectivity. because Unity IAP never tells offline.
			*/
			if (Application.internetReachability == NetworkReachability.NotReachable) {
				failedToReady(PurchaseError.Offline, "network is offline.", new AutoyaStatus());
				return;
			}
			
			UnityPurchasing.Initialize(this, builder);
		}
		
        
		private IStoreController controller;
		private IExtensionProvider extensions;
		
		public bool IsPurchaseReady () {
			return routerState == RouterState.PurchaseReady;
		}

		/// <summary>
		/// Called when Unity IAP is ready to make purchases.
		/// </summary>
		public void OnInitialized (IStoreController controller, IExtensionProvider extensions) {
			this.controller = controller;
			this.extensions = extensions;

			routerState = RouterState.PurchaseReady;
			if (readyPurchase != null) {
				readyPurchase();
			}
		}

		/// <summary>
		/// Called when Unity IAP encounters an unrecoverable initialization error.
		///
		/// Note that this will not be called if Internet is unavailable; Unity IAP
		/// will attempt initialization until it becomes available.
		/// </summary>
		public void OnInitializeFailed (InitializationFailureReason error) {
			routerState = RouterState.FailedToLoadStore;
			switch (error) {
				case InitializationFailureReason.AppNotKnown: {
					failedToReady(PurchaseError.UnityIAP_Initialize_AppNowKnown, "The store reported the app as unknown.", new AutoyaStatus());
					break;
				}
				case InitializationFailureReason.NoProductsAvailable: {
					failedToReady(PurchaseError.UnityIAP_Initialize_NoProductsAvailable, "No products available for purchase.", new AutoyaStatus());
					break;
				}
				case InitializationFailureReason.PurchasingUnavailable: {
					failedToReady(PurchaseError.UnityIAP_Initialize_PurchasingUnavailable, "In-App Purchases disabled in device settings.", new AutoyaStatus());
					break;
				}
			}
		}
		
		/**
			start purchase.
		*/
		public void Purchase (
			string purchaseId, 
			string productId, 
			Action<string> purchaseSucceeded, 
			Action<string, PurchaseError, string, AutoyaStatus> purchaseFailed
		) {
			if (Application.internetReachability == NetworkReachability.NotReachable) {
				purchaseFailed(purchaseId, PurchaseError.Offline, "network is offline.", new AutoyaStatus());
				return;
			}

			if (routerState != RouterState.PurchaseReady) {
				switch (routerState) {
					case RouterState.GettingTransaction:
					case RouterState.Purchasing: {
						purchaseFailed(purchaseId, PurchaseError.AlreadyPurchasing, "purchasing another product now. wait then retry.", new AutoyaStatus());
						break;
					}
					default: {
						purchaseFailed(purchaseId, PurchaseError.UnknownError, "state is:" + routerState, new AutoyaStatus());
						break;
					}
				}
				return;
			}

			
			if (verifiedProducts != null) {
				var verified = false;
				foreach (var verifiedProduct in verifiedProducts) {
					if (verifiedProduct.productId == productId && verifiedProduct.isAvailableToThisPlayer) {
						verified = true;
					}
				}
				
				if (!verified) {
					purchaseFailed(purchaseId, PurchaseError.UnavailableProduct, "this product is not available.", new AutoyaStatus());
					return;
				}

                var product = this.controller.products.WithID(productId);
                if (product != null) {
                    if (product.availableToPurchase) {
                        routerState = RouterState.Purchasing;
                                
                        // renew callback.
                        callbacks = new Callbacks(product, purchaseId, string.Empty, purchaseSucceeded, purchaseFailed);
                        this.controller.InitiatePurchase(product);
                    } else {
                        purchaseFailed(purchaseId, PurchaseError.UnavailableProduct, "selected product is not available.", new AutoyaStatus());
                        routerState = RouterState.PurchaseReady;
                    }
                }
			}
		}
		
		private struct Callbacks {
			public readonly Product p;
			public readonly string ticketId;
			public readonly Action purchaseSucceeded;
			public readonly Action<PurchaseError, string, AutoyaStatus> purchaseFailed;
			public Callbacks (Product p, string purchaseId, string ticketId, Action<string> purchaseSucceeded, Action<string, PurchaseError, string, AutoyaStatus> purchaseFailed) {
				this.p = p;
				this.ticketId = ticketId;
				this.purchaseSucceeded = () => {
					purchaseSucceeded(purchaseId);
				};
				this.purchaseFailed = (err, reason, autoyaStatus) => {
					purchaseFailed(purchaseId, err, reason, autoyaStatus);
				};
			}
		}

		private Callbacks callbacks = new Callbacks(null, string.Empty, string.Empty, pId => {}, (tId, error, reason, autoyaStatus) => {});
		
		/// <summary>
		/// Called when a purchase completes.
		///
		/// May be called at any time after OnInitialized().
		/// </summary>
		public PurchaseProcessingResult ProcessPurchase (PurchaseEventArgs e) {
            if (callbacks.p == null) {
				var isValid = ValidateReceipt(e);
                if (isValid) {
					purchasedInBackground(e.purchasedProduct.definition.id);
				}
				return PurchaseProcessingResult.Complete;
			}

			if (e.purchasedProduct.transactionID != callbacks.p.transactionID) {
				var isValid = ValidateReceipt(e);
				if (isValid) {
					purchasedInBackground(e.purchasedProduct.definition.id);
				}
				return PurchaseProcessingResult.Complete;
			}
            
			/*
				this process is the process for the purchase which this router is just retrieving.
				proceed deploy asynchronously.
			*/
			switch (routerState) {
				case RouterState.Purchasing: {
					// start local validation.
                    var isValid = ValidateReceipt(e);

                    if (isValid) {
                        if (callbacks.purchaseSucceeded != null) {
                            callbacks.purchaseSucceeded();
                        }
                    } else {
                        if (callbacks.purchaseFailed != null) {
                            callbacks.purchaseFailed(PurchaseError.InvalidReceipt, "receipt validation failed. state:" + routerState, new AutoyaStatus());
                        }
                    }

					routerState = RouterState.PurchaseReady;

                    // complete anyway. nothing to do.
                    return PurchaseProcessingResult.Complete;
				}
				default: {
					if (callbacks.purchaseFailed != null) {
						callbacks.purchaseFailed(PurchaseError.UnknownError, "failed to deploy purchased item 3. state:" + routerState, new AutoyaStatus());
					}
					break;
				}
			}

			/*
				always pending.
			*/
			return PurchaseProcessingResult.Pending;
		}

        private bool ValidateReceipt (PurchaseEventArgs e) {
            #if UNITY_ANDROID || UNITY_IOS
                var validator = new CrossPlatformValidator(GooglePlayTangle.Data(), AppleTangle.Data(), Application.identifier);

                try {
                    validator.Validate(e.purchasedProduct.receipt);

                    // if no exception, it's ok.
                    return true;
                } catch (IAPSecurityException) {}
			#else
				Debug.LogWarning("this local purchase feature is enable only for iOS/Android. in other platform, validation result is always false. please change platform to iOS/Android then set both AppleTangle & GooglePlayTangle in Editor. see https://docs.unity3d.com/Manual/UnityIAPValidatingReceipts.html");
            #endif
            return false;
        }

		/// <summary>
		/// Called when a purchase fails.
		/// </summary>
		public void OnPurchaseFailed (Product i, PurchaseFailureReason failReason) {
			// no retrieving product == not purchasing.
			if (callbacks.p == null) {
				// do nothing here.
				return;
			}

			// transactionID does not match to retrieving product's transactionID,
			// it's not the product which should be notice to user.
			if (i.transactionID != callbacks.p.transactionID) {
				// do nothing here.
				return;
			}

			/*
				this purchase failed product is just retrieving purchase.
			*/

			/*
				detect errors.
			*/
			var error = PurchaseError.UnityIAP_Purchase_Unknown;
			var reason = string.Empty;
			
			switch (failReason) {
				case PurchaseFailureReason.PurchasingUnavailable: {
					error = PurchaseError.UnityIAP_Purchase_PurchasingUnavailable;
					reason = "The system purchasing feature is unavailable.";
					break;
				}
				case PurchaseFailureReason.ExistingPurchasePending: {
					error = PurchaseError.UnityIAP_Purchase_ExistingPurchasePending;
					reason = "A purchase was already in progress when a new purchase was requested.";
					break;
				}
				case PurchaseFailureReason.ProductUnavailable: {
					error = PurchaseError.UnityIAP_Purchase_ProductUnavailable;
					reason = "The product is not available to purchase on the store.";
					break;
				}
				case PurchaseFailureReason.SignatureInvalid: {
					error = PurchaseError.UnityIAP_Purchase_SignatureInvalid;
					reason = "Signature validation of the purchase's receipt failed.";
					break;
				}
				case PurchaseFailureReason.UserCancelled: {
					error = PurchaseError.UnityIAP_Purchase_UserCancelled;
					reason = "The user opted to cancel rather than proceed with the purchase.";
					break;
				}
				case PurchaseFailureReason.PaymentDeclined: {
					error = PurchaseError.UnityIAP_Purchase_PaymentDeclined;
					reason = "There was a problem with the payment.";
					break;
				}
				case PurchaseFailureReason.Unknown: {
					error = PurchaseError.UnityIAP_Purchase_Unknown;
					reason = "A catch-all for unrecognized purchase problems.";
					break;
				}
			}

			switch (routerState) {
				default: {
					Debug.LogError("ここにくるケースを見切れていない2。");
					if (callbacks.purchaseFailed != null) { 
						callbacks.purchaseFailed(error, reason, new AutoyaStatus());
					}
					break;
				}
				case RouterState.Purchasing: {
					if (callbacks.purchaseFailed != null) {
						callbacks.purchaseFailed(error, reason, new AutoyaStatus());
					}
					routerState = RouterState.PurchaseReady;
					break;
				}
			}
		}
	}
}