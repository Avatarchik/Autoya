using System;
using System.Collections;
using AutoyaFramework;
using AutoyaFramework.AssetBundles;
using AutoyaFramework.Settings.AssetBundles;
using UnityEngine;
using UnityEngine.UI;

public class LoadResourcesSample : MonoBehaviour
{
    public Image image;
    private Sprite sprite;

    void Start()
    {
        /*
            Both AssetBundleLoadAsset method and Resources_LoadAsset method has same signature.
            you can replace the load-source of assets from Resources to AssetBundle easily.

            basically this method is async.
         */
        Autoya.Resources_LoadAsset<Sprite>(
            "SampleResource/shisyamo",
            (assetName, sprite) =>
            {
                Debug.Log("asset:" + assetName + " is successfully loaded as:" + sprite);

                image.sprite = sprite;
            },
            (assetName, err, reason, autoyaStatus) =>
            {
                Debug.LogError("failed to load assetName:" + assetName + " err:" + err + " reason:" + reason + " autoyaStatus:" + autoyaStatus);
            }
        );
    }

    void OnApplicationQuit()
    {
        Autoya.Resources_Unload(sprite);
    }

}

