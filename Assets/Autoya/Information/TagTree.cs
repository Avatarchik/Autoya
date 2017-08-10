using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace AutoyaFramework.Information {
    
    public enum TreeType {
        Container,
        Content_Text,
        Content_Img,
        Content_CRLF,
        CustomLayer,
        CustomBox,
        CustomEmptyLayer,
    }

    /**
        tree structure.
     */
    public class TagTree {
        // tree params.
        private List<TagTree> _children = new List<TagTree>();
        
        // tag params.
        public readonly int tagValue;
        public readonly AttributeKVs keyValueStore;
        public readonly TreeType treeType;

        private bool hidden = false;

        public void ShowOrHide () {
            hidden = !hidden;

            // 更新
            keyValueStore[HTMLAttribute.HIDDEN] = hidden.ToString();
            
            if (hidden) {
                SetHide();
            }
        }
        public void SetHide () {
            hidden = true;

            offsetX = 0;
            offsetY = 0;
            viewWidth = 0;
            viewHeight = 0;
        }

        public bool IsHidden () {
            return hidden;
        }

        // レイアウト処理
        public float offsetX;
        public float offsetY;
        public float viewWidth;
        public float viewHeight;

        public ViewCursor SetPos (float offsetX, float offsetY, float viewWidth, float viewHeight) {
            this.offsetX = offsetX;
            this.offsetY = offsetY;
            this.viewWidth = viewWidth;
            this.viewHeight = viewHeight;
            return new ViewCursor(offsetX, offsetY, viewWidth, viewHeight);
        }

        public void SetPosFromViewCursor (ViewCursor source) {
            this.offsetX = source.offsetX;
            this.offsetY = source.offsetY;
            this.viewWidth = source.viewWidth;
            this.viewHeight = source.viewHeight;
        }

        public TagTree () {
            this.tagValue = (int)HTMLTag._ROOT;
            this.keyValueStore = new AttributeKVs();
            this.treeType = TreeType.Container;
        }

        public TagTree (string textContent, int baseTagValue) {// as text_content.
            this.tagValue = baseTagValue;
            
            this.keyValueStore = new AttributeKVs();
            keyValueStore[HTMLAttribute._CONTENT] = textContent;
            
            this.treeType = TreeType.Content_Text;
        }

        public TagTree (int parsedTag, AttributeKVs kv, TreeType treeType) {
            this.tagValue = parsedTag;
            this.keyValueStore = kv;
            this.treeType = treeType;
        }
        
        public void SetParent (TagTree t) {
            if (
                t.tagValue == (int)HTMLTag._ROOT && 
                this.treeType == TreeType.Content_Text
            ) {
                var val = this.keyValueStore[HTMLAttribute._CONTENT];
                throw new Exception("invalid text contains outside of tag. val:" + val);
            }
            
			t._children.Add(this);
		}

        public List<TagTree> GetChildren () {
			return _children;
		}


        public void AddChildren (TagTree[] children) {
            this._children.AddRange(children);
        }

        public void RemoveChild (TagTree child) {
            this._children.Remove(child);
        }

        public void ReplaceChildrenToBox (TagTree[] oldTrees, TagTree newTree) {
            foreach (var oldTree in oldTrees) {
                this._children.Remove(oldTree);
            }

            this._children.Add(newTree);
        }

        public string ShowContent() {
            return this.treeType.ToString();
        }


        /**
            uGUIのパラメータからRectを出す。
         */
        public static Rect GetChildViewRectFromParentRectTrans (float parentWidth, float parentHeight, BoxPos pos) {
            // アンカーからwidthとheightを出す。
            var anchorWidth = (parentWidth * pos.anchorMin.x) + (parentWidth * (1 - pos.anchorMax.x));
            var anchorHeight = (parentHeight * pos.anchorMin.y) + (parentHeight * (1 - pos.anchorMax.y));

            var viewWidth = parentWidth - anchorWidth - pos.offsetMin.x + pos.offsetMax.x;
            var viewHeight = parentHeight - anchorHeight - pos.offsetMin.y + pos.offsetMax.y;

            // 左上原点を出す。
            var offsetX = (parentWidth * pos.anchorMin.x) + pos.offsetMin.x;
            var offsetY = (parentHeight * (1-pos.anchorMax.y)) - (pos.offsetMax.y);

            return new Rect(offsetX, offsetY, viewWidth, viewHeight);
        }

         public static Vector2 AnchoredPositionOf (TagTree tree) {
            return new Vector2(tree.offsetX, -tree.offsetY);
        }

        public static Vector2 SizeDeltaOf (TagTree tree) {
            return new Vector2(tree.viewWidth, tree.viewHeight);
        }

        public static TagTree RevertInsertedTree (TagTree rootTree) {
			RevertRecursive(rootTree);
            return rootTree;
		}

        private static void RevertRecursive (TagTree tree) {
            var children = tree.GetChildren();
            children.Reverse();

            var count = children.Count;
            
            var list = new List<TagTree>();
            for (var i = 0; i < count; i++) {
                var child = children[i];
                RevertRecursive(child);

                if (child is InsertedTree) {
                    var insertedTree = child as InsertedTree;
                    var baseTree = insertedTree.parentTree;

                    // merge contents.
                    baseTree.keyValueStore[HTMLAttribute._CONTENT] = baseTree.keyValueStore[HTMLAttribute._CONTENT] as string + insertedTree.keyValueStore[HTMLAttribute._CONTENT] as string;
                    
                    list.Add(insertedTree);
                }
            }

            foreach (var removedInsertedTree in list) {
                tree.RemoveChild(removedInsertedTree);
            }
        }
    }

    public class InsertedTree : TagTree {
        public readonly TagTree parentTree;
        public InsertedTree (TagTree baseTree, string textContent, int baseTag) : base(textContent, baseTag) {
            this.parentTree = baseTree;
        }
    }
}