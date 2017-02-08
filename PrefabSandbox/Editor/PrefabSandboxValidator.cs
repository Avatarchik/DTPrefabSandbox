using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DTPrefabSandbox {
    public class PrefabSandboxValidator : IDisposable {
        // PRAGMA MARK - Public Interface
        public void Dispose() {
            this._prefab = null;
            this._cachedValidationErrors = null;

            EditorApplicationUtil.OnSceneGUIDelegate -= this.OnSceneGUI;
            EditorApplicationUtil.SceneDirtied -= this.RefreshValidationErrors;
            EditorApplication.hierarchyWindowItemOnGUI -= this.OnHierarchyWindowItemOnGUI;
        }

        public PrefabSandboxValidator(GameObject prefab) {
            this._prefab = prefab;
            this.RefreshValidationErrors();

            EditorApplicationUtil.OnSceneGUIDelegate += this.OnSceneGUI;
            EditorApplicationUtil.SceneDirtied += this.RefreshValidationErrors;
            EditorApplication.hierarchyWindowItemOnGUI += this.OnHierarchyWindowItemOnGUI;
        }

        public bool RefreshAndCheckValiationErrors() {
            this.RefreshValidationErrors();
            return !this._cachedValidationErrors.IsNullOrEmpty();
        }


        // PRAGMA MARK - Internal
        private const float kErrorHeight = 20.0f;
        private const float kErrorSpacingHeight = 2.0f;
        private const float kErrorWidth = 275.0f;
        private const float kLinkWidth = 40.0f;
        private const float kLinkPadding = 3.0f;

        private static GUIStyle _kButtonStyle = null;
        private static GUIStyle kButtonStyle {
            get {
                // NOTE (darren): sometimes the textures can get dealloc
                // and appear as nothing - we recreate them here
                if (_kButtonStyle != null && _kButtonStyle.normal.background == null) {
                    _kButtonStyle = null;
                }

                if (_kButtonStyle == null) {
                    _kButtonStyle = new GUIStyle(GUI.skin.GetStyle("Button"));
                    _kButtonStyle.alignment = TextAnchor.MiddleRight;
                    _kButtonStyle.padding.right = (int)(kLinkWidth + (2.0f * kLinkPadding) + 2);
                    _kButtonStyle.padding.top = 3;
                    _kButtonStyle.normal.background = Texture2DUtil.GetCached1x1TextureWithColor(Color.black.WithAlpha(0.5f));
                    _kButtonStyle.active.background = Texture2DUtil.GetCached1x1TextureWithColor(Color.black.WithAlpha(0.3f));
                }
                return _kButtonStyle;
            }
        }

        private const float kErrorIconPadding = 3.0f;

        private static Texture2D _kErrorIconTexture = null;
        private static Texture2D kErrorIconTexture {
            get {
                if (_kErrorIconTexture == null) {
                    string prefabSandboxPath = ScriptableObjectEditorUtil.PathForScriptableObjectType<PrefabSandboxMarker>();
                    _kErrorIconTexture = AssetDatabaseUtil.LoadAssetAtPath<Texture2D>(prefabSandboxPath + "/Icons/ErrorIcon.png");// ?? new Texture2D(0, 0);
                }
                return _kErrorIconTexture;
            }
        }

        private GameObject _prefab;
        private IList<GameObjectValidator.ValidationError> _cachedValidationErrors;
        private HashSet<GameObject> _objectsWithErrors = new HashSet<GameObject>();

        private void OnSceneGUI(SceneView sceneView) {
            if (this._cachedValidationErrors == null) {
                return;
            }

            Handles.BeginGUI();

            // BEGIN SCENE GUI
            float yPosition = 0.0f;
            foreach (GameObjectValidator.ValidationError error in this._cachedValidationErrors) {
                // NOTE (darren): it's possible that OnSceneGUI gets called after
                // the prefab is destroyed - don't do anything in that case
                if (error.component == null) {
                    continue;
                }

                var linkRect = new Rect(kErrorWidth - kLinkWidth - kLinkPadding, yPosition + kLinkPadding, kLinkWidth, kErrorHeight - kLinkPadding);
                if (GUI.Button(linkRect, "Link")) {
                    LinkValidationError(error);
                }

                var oldContentColor = GUI.contentColor;
                GUI.contentColor = PrefabSandbox.kErrorColor;

                var rect = new Rect(0.0f, yPosition, kErrorWidth, kErrorHeight);
                var errorDescription = string.Format("{0}->{1}.{2}", error.component.gameObject.name, error.componentType.Name, error.fieldInfo.Name);

                if (GUI.Button(rect, errorDescription, kButtonStyle)) {
                    Selection.activeGameObject = error.component.gameObject;
                }

                GUI.contentColor = oldContentColor;

                if (GUI.Button(linkRect, "Link")) {
                    // empty (no-action) button for the visual look
                    // NOTE (darren): it appears the order in which GUI.button is
                    // called determines the ordering for which button consumes the touch
                    // but also the order is used to render :)
                }

                yPosition += kErrorHeight + kErrorSpacingHeight;
            }
            // END SCENE GUI
            Handles.EndGUI();
        }

        private void OnHierarchyWindowItemOnGUI(int instanceId, Rect selectionRect) {
            if (Event.current.type != EventType.Repaint) {
                return;
            }

            GameObject g = EditorUtility.InstanceIDToObject(instanceId) as GameObject;

            bool gameObjectHasError = this._objectsWithErrors.Contains(g);
            if (gameObjectHasError) {
                float edgeLength = selectionRect.height - (2.0f * kErrorIconPadding);
                Rect errorIconRect = new Rect(selectionRect.x + selectionRect.width - kErrorIconPadding - edgeLength,
                selectionRect.y + kErrorIconPadding,
                edgeLength,
                edgeLength);
                GUI.DrawTexture(errorIconRect, kErrorIconTexture);
                EditorApplication.RepaintHierarchyWindow();
            }
        }

        private void RefreshValidationErrors() {
            this._cachedValidationErrors = GameObjectValidator.Validate(this._prefab);

            this._objectsWithErrors.Clear();
            if (this._cachedValidationErrors != null) {
                foreach (GameObjectValidator.ValidationError error in this._cachedValidationErrors) {
                    this._objectsWithErrors.Add(error.component.gameObject);
                }
            }
        }

        private void LinkValidationError(GameObjectValidator.ValidationError error) {
            var selectedGameObject = Selection.activeGameObject;
            if (selectedGameObject == null) {
                Debug.LogWarning("Cannot link when no selected game object!");
                return;
            }

            var fieldType = error.fieldInfo.FieldType;
            if (fieldType == typeof(UnityEngine.GameObject)) {
                error.fieldInfo.SetValue(error.component, selectedGameObject);
            } else if (typeof(UnityEngine.Component).IsAssignableFrom(fieldType)) {
                var linkedComponent = selectedGameObject.GetComponent(fieldType);
                if (linkedComponent == null) {
                    Debug.LogWarning("LinkValidationError: Failed to find component of type: " + fieldType.Name + " on selected game object, cannot link!");
                    return;
                }

                error.fieldInfo.SetValue(error.component, linkedComponent);
            } else {
                Debug.LogWarning("LinkValidationError: Field is of unhandled type: " + fieldType.Name + ", cannot link!");
                return;
            }

            SceneView.RepaintAll();
            EditorUtility.SetDirty(error.component);
            RefreshValidationErrors();
        }
    }
}
