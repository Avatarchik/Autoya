﻿using AutoyaFramework;
using AutoyaFramework.AssetBundles;
using UnityEngine;

public class LoadAssetBundle : MonoBehaviour {
	private const string basePath = "https://dl.dropboxusercontent.com/u/36583594/outsource/Autoya/AssetBundle/AssetBundles/";

	// Use this for initialization
	void Start () {
		var dummyList = new AssetBundleList("1.0.0", 
			new AssetBundleInfo[]{
				// pngが一枚入ったAssetBundle
				new AssetBundleInfo(
					"bundlename", 
					new string[]{"Assets/AutoyaTests/Runtime/AssetBundles/TestResources/textureName.png"}, 
					new string[0], 
					621985162
				),
				// 他のAssetBundleへの依存があるAssetBundle
				new AssetBundleInfo(
					"dependsbundlename", 
					new string[]{"Assets/AutoyaTests/Runtime/AssetBundles/TestResources/textureName1.prefab"}, 
					new string[]{"bundlename"}, 
					2389728195
				),
				// もう一つ、他のAssetBundleへの依存があるAssetBundle
				new AssetBundleInfo(
					"dependsbundlename2", 
					new string[]{"Assets/AutoyaTests/Runtime/AssetBundles/TestResources/textureName2.prefab"}, 
					new string[]{"bundlename"}, 
					1194278944
				),
				// nestedprefab -> dependsbundlename -> bundlename
				new AssetBundleInfo(
					"nestedprefab", 
					new string[]{"Assets/AutoyaTests/Runtime/AssetBundles/TestResources/nestedPrefab.prefab"}, 
					new string[]{"dependsbundlename"}, 
					779842307
				),
				
			}
		);

		// loadList -> preload assetBundles -> load asset.
		Autoya.AssetBundle_UpdateList(basePath, dummyList);

		Autoya.AssetBundle_LoadAsset(
			"Assets/AutoyaTests/Runtime/AssetBundles/TestResources/nestedPrefab.prefab",
			(string assetName, GameObject prefab) => {
				Instantiate(prefab);
			},
			(assetName, err, reason) => {
				Debug.LogError("failed to load assetName:" + assetName + " err:" + err + " reason:" + reason);
			}
		);
	}
	
}
