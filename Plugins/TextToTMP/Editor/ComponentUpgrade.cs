using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TextToTMPNamespace
{
	public partial class TextToTMPWindow
	{
		#region Helper Classes
		private class SelectableObjectProperties
		{
			private readonly AnimationTriggers animationTriggers;
			private readonly ColorBlock colors;
			private readonly Image image;
			private readonly bool interactable;
			private readonly Navigation navigation;
			private readonly SpriteState spriteState;
			private readonly Graphic targetGraphic;
			private readonly Selectable.Transition transition;

			public SelectableObjectProperties( Selectable selectable )
			{
				animationTriggers = selectable.animationTriggers;
				colors = selectable.colors;
				image = selectable.image;
				interactable = selectable.interactable;
				navigation = selectable.navigation;
				spriteState = selectable.spriteState;
				targetGraphic = selectable.targetGraphic;
				transition = selectable.transition;
			}

			public void ApplyTo( Selectable selectable )
			{
				selectable.animationTriggers = animationTriggers;
				selectable.colors = colors;
				selectable.image = image;
				selectable.interactable = interactable;
				selectable.navigation = navigation;
				selectable.spriteState = spriteState;
				selectable.targetGraphic = targetGraphic;
				selectable.transition = transition;
			}
		}
		#endregion

		private bool alwaysUseOverflowForNonWrappingTexts = false;

		private readonly HashSet<string> upgradedPrefabs = new HashSet<string>();

		private FieldInfo unityEventPersistentCallsField;

		private void UpgradeComponents()
		{
			upgradedPrefabs.Clear();

			stringBuilder.Length = 0;

			int progressCurrent = 0;
			int progressTotal = assetsToUpgrade.EnabledCount + scenesToUpgrade.EnabledCount;
			try
			{
				foreach( string asset in assetsToUpgrade )
				{
					EditorUtility.DisplayProgressBar( "Upgrading components in assets...", asset, (float) progressCurrent / progressTotal );
					progressCurrent++;

					if( asset.EndsWith( ".prefab", StringComparison.OrdinalIgnoreCase ) )
						UpgradeComponentsInPrefab( asset );
				}

				foreach( string scene in scenesToUpgrade )
				{
					EditorUtility.DisplayProgressBar( "Upgrading components in scenes...", scene, (float) progressCurrent / progressTotal );
					progressCurrent++;

					UpgradeComponentsInScene( SceneManager.GetSceneByPath( scene ) );
				}
			}
			finally
			{
				if( stringBuilder.Length > 0 )
				{
					stringBuilder.Insert( 0, "<b>Upgrade Components Logs:</b>\n" );
					Debug.Log( stringBuilder.ToString() );
				}

				EditorUtility.ClearProgressBar();

				AssetDatabase.SaveAssets();
				EditorSceneManager.SaveOpenScenes();
			}
		}

		private void UpgradeComponentsInScene( Scene scene )
		{
			if( !scene.IsValid() )
			{
				Debug.LogError( "Scene " + scene.name + " is not valid, can't uprade components inside it!" );
				return;
			}

			if( !scene.isLoaded )
			{
				Debug.LogError( "Scene " + scene.name + " is not loaded, can't uprade components inside it!" );
				return;
			}

			GameObject[] rootGameObjects = scene.GetRootGameObjects();
			for( int i = 0; i < rootGameObjects.Length; i++ )
				UpgradeGameObjectRecursively( rootGameObjects[i] );

			EditorSceneManager.MarkSceneDirty( scene );
		}

		private void UpgradeComponentsInPrefab( string prefabPath )
		{
			if( string.IsNullOrEmpty( prefabPath ) )
				return;

			if( upgradedPrefabs.Contains( prefabPath ) )
				return;

			upgradedPrefabs.Add( prefabPath );

			GameObject prefab = AssetDatabase.LoadMainAssetAtPath( prefabPath ) as GameObject;
			if( !prefab )
				return;

			if( ( prefab.hideFlags & HideFlags.NotEditable ) == HideFlags.NotEditable )
				return;

#if UNITY_2018_3_OR_NEWER
			PrefabAssetType prefabAssetType = PrefabUtility.GetPrefabAssetType( prefab );
			if( prefabAssetType != PrefabAssetType.Regular && prefabAssetType != PrefabAssetType.Variant )
				return;
#else
			PrefabType prefabAssetType = PrefabUtility.GetPrefabType( prefab );
			if( prefabAssetType != PrefabType.Prefab )
				return;
#endif

			// Check if prefab has any upgradeable components
			bool isUpgradeable = prefab.GetComponentInChildren<TextMesh>( true );
			if( !isUpgradeable )
			{
				UIBehaviour[] graphicComponents = prefab.GetComponentsInChildren<UIBehaviour>( true );
				if( graphicComponents != null )
				{
					for( int i = 0; i < graphicComponents.Length; i++ )
					{
						if( graphicComponents[i] && ( graphicComponents[i] is Text || graphicComponents[i] is InputField || graphicComponents[i] is Dropdown ) )
						{
							isUpgradeable = true;
							break;
						}
					}
				}
			}

			if( !isUpgradeable )
				return;

			// Instantiate the prefab, we need instances to change children's parents (needed in InputField's upgrade)
#if UNITY_2018_3_OR_NEWER
			GameObject prefabInstanceRoot = PrefabUtility.LoadPrefabContents( prefabPath );
#else
			GameObject prefabInstanceRoot = (GameObject) PrefabUtility.InstantiatePrefab( prefab );
			PrefabUtility.DisconnectPrefabInstance( prefabInstanceRoot );
#endif
			try
			{
				UpgradeGameObjectRecursively( prefabInstanceRoot );

#if UNITY_2018_3_OR_NEWER
				prefab = PrefabUtility.SaveAsPrefabAsset( prefabInstanceRoot, prefabPath );
#else
				prefab = PrefabUtility.ReplacePrefab( prefabInstanceRoot, prefab, ReplacePrefabOptions.ConnectToPrefab );
#endif
			}
			finally
			{
#if UNITY_2018_3_OR_NEWER
				PrefabUtility.UnloadPrefabContents( prefabInstanceRoot );
#else
				DestroyImmediate( prefabInstanceRoot );
#endif
			}

			EditorUtility.SetDirty( prefab );
		}

		private void UpgradeGameObjectRecursively( GameObject go )
		{
#if UNITY_2018_3_OR_NEWER
			// Upgrade encountered prefab assets
			if( PrefabUtility.IsAnyPrefabInstanceRoot( go ) )
				UpgradeComponentsInPrefab( PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot( go ) );
#endif

			UpgradeDropdown( go.GetComponent<Dropdown>() );
			UpgradeInputField( go.GetComponent<InputField>() );
			UpgradeText( go.GetComponent<Text>() );
			UpgradeTextMesh( go.GetComponent<TextMesh>() );

			for( int i = 0; i < go.transform.childCount; i++ )
				UpgradeGameObjectRecursively( go.transform.GetChild( i ).gameObject );
		}

		private TextMeshProUGUI UpgradeText( Text text )
		{
			if( !text )
				return null;

			GameObject go = text.gameObject;
			stringBuilder.AppendLine( "Upgrading Text: " + GetPathOfObject( go.transform ) );

			// Copy fields
			Vector2 sizeDelta = text.rectTransform.sizeDelta;

			TextAlignmentOptions alignment = GetTMPAlignment( text.alignment, text.alignByGeometry );
			bool bestFit = text.resizeTextForBestFit;
			int bestFitMaxSize = text.resizeTextMaxSize;
			int bestFitMinSize = text.resizeTextMinSize;
			Color color = text.color;
			bool enabled = text.enabled;
			Material fontMaterial;
			TMP_FontAsset font = GetCorrespondingTMPFontAsset( text.font, text, out fontMaterial );
			int fontSize = text.fontSize;
			FontStyles fontStyle = GetTMPFontStyle( text.fontStyle );
			bool horizontalWrapMode = text.horizontalOverflow == HorizontalWrapMode.Wrap;
			float lineSpacing = ( text.lineSpacing - 1 ) * 100f;
			bool raycastTarget = text.raycastTarget;
			bool supportRichText = text.supportRichText;
			string _text = text.text;
			TextOverflowModes verticalOverflow = GetTMPVerticalOverflow( text.verticalOverflow, text.horizontalOverflow );

			// Replace Text with TextMeshProUGUI
			DestroyImmediate( text, true );
			TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();

			// Paste fields
			tmp.alignment = alignment;
			tmp.enableAutoSizing = bestFit;
			tmp.fontSizeMax = bestFitMaxSize;
			tmp.fontSizeMin = bestFitMinSize;
			tmp.color = color;
			tmp.enabled = enabled;
			tmp.font = font;
			tmp.fontMaterial = fontMaterial;
			tmp.fontSize = fontSize;
			tmp.fontStyle = fontStyle;
			tmp.enableWordWrapping = horizontalWrapMode;
			tmp.lineSpacing = lineSpacing;
			tmp.raycastTarget = raycastTarget;
			tmp.richText = supportRichText;
			tmp.text = _text;
			tmp.overflowMode = verticalOverflow;

			tmp.rectTransform.sizeDelta = sizeDelta;

			return tmp;
		}

		private TextMeshPro UpgradeTextMesh( TextMesh text )
		{
			if( !text )
				return null;

			GameObject go = text.gameObject;
			stringBuilder.AppendLine( "Upgrading TextMesh: " + GetPathOfObject( go.transform ) );

			// Copy fields
			TextAlignmentOptions alignment = GetTMPAlignment( text.anchor, false );
			float characterSize = text.characterSize;
			Color color = text.color;
			Material fontMaterial;
			TMP_FontAsset font = GetCorrespondingTMPFontAsset( text.font, text, out fontMaterial );
			int fontSize = text.fontSize > 0 ? text.fontSize : 13;
			FontStyles fontStyle = GetTMPFontStyle( text.fontStyle );
			float lineSpacing = ( text.lineSpacing - 1 ) * 100f;
			float offsetZ = text.offsetZ;
			bool richText = text.richText;
			string _text = text.text;

			// Replace Text with TextMeshProUGUI
			DestroyImmediate( text, true );
			TextMeshPro tmp = go.AddComponent<TextMeshPro>();

			// Paste fields
			tmp.alignment = alignment;
			tmp.color = color;
			tmp.font = font;
			tmp.fontMaterial = fontMaterial;
			tmp.fontSize = fontSize;
			tmp.fontStyle = fontStyle;
			tmp.lineSpacing = lineSpacing;
			tmp.richText = richText;
			tmp.text = _text;

			tmp.enableWordWrapping = false;
			tmp.overflowMode = TextOverflowModes.Overflow;
			tmp.rectTransform.sizeDelta = Vector2.zero;
			tmp.rectTransform.localScale *= characterSize;
			tmp.rectTransform.Translate( new Vector3( 0f, 0f, offsetZ ) );

			return tmp;
		}

		private TMP_InputField UpgradeInputField( InputField inputField )
		{
			if( !inputField )
				return null;

			GameObject go = inputField.gameObject;
			stringBuilder.AppendLine( "Upgrading InputField: " + GetPathOfObject( go.transform ) );

			// Copy fields
			Vector2 sizeDelta = ( (RectTransform) inputField.transform ).sizeDelta;
			SelectableObjectProperties selectableProperties = new SelectableObjectProperties( inputField );

			char asteriskChar = inputField.asteriskChar;
			float caretBlinkRate = inputField.caretBlinkRate;
			bool customCaretColor = inputField.customCaretColor;
			Color? caretColor = null;
			try { caretColor = inputField.caretColor; } catch { }
			float caretWidth = inputField.caretWidth;
			int characterLimit = inputField.characterLimit;
			TMP_InputField.CharacterValidation characterValidation = GetTMPCharacterValidation( inputField.characterValidation );
			TMP_InputField.ContentType contentType = GetTMPContentType( inputField.contentType );
			bool enabled = inputField.enabled;
			TMP_InputField.InputType inputType = GetTMPInputType( inputField.inputType );
			TouchScreenKeyboardType keyboardType = inputField.keyboardType;
			TMP_InputField.LineType lineType = GetTMPLineType( inputField.lineType );
			bool readOnly = inputField.readOnly;
			Color selectionColor = inputField.selectionColor;
			bool shouldHideMobileInput = inputField.shouldHideMobileInput;
			string _text = inputField.text;

			// Copy UnityEvents
			object onEndEdit = CopyUnityEvent( inputField.onEndEdit );
#if UNITY_5_3_OR_NEWER
			object onValueChanged = CopyUnityEvent( inputField.onValueChanged );
#else
			object onValueChanged = CopyUnityEvent( inputField.onValueChange );
#endif

			// Upgrade&copy child objects
			TextMeshProUGUI textComponent = UpgradeText( inputField.textComponent );
			Graphic placeholderComponent = ( inputField.placeholder as Text ) ? UpgradeText( (Text) inputField.placeholder ) : inputField.placeholder;

			// Replace InputField with TMP_InputField
			DestroyImmediate( inputField, true );
			TMP_InputField tmp = go.AddComponent<TMP_InputField>();

			// Paste child objects
			tmp.textComponent = textComponent;
			tmp.placeholder = placeholderComponent;

			// Paste fields
			selectableProperties.ApplyTo( tmp );

			tmp.asteriskChar = asteriskChar;
			tmp.caretBlinkRate = caretBlinkRate;
			tmp.customCaretColor = customCaretColor;
			try { if( caretColor.HasValue ) tmp.caretColor = caretColor.Value; } catch { }
			tmp.caretWidth = Mathf.RoundToInt( caretWidth );
			tmp.characterLimit = characterLimit;
			tmp.characterValidation = characterValidation;
			tmp.contentType = contentType;
			tmp.enabled = enabled;
			tmp.inputType = inputType;
			tmp.keyboardType = keyboardType;
			if( tmp.lineType == lineType ) tmp.lineType = (TMP_InputField.LineType) ( ( (int) lineType + 1 ) % 3 ); // lineType adjusts Text's overflow settings but only if the lineType value is different
			tmp.lineType = lineType;
			if( textComponent ) textComponent.overflowMode = TextOverflowModes.Overflow; // lineType doesn't modify this value, though. If must be set to Overflow for TMP_InputField texts
			tmp.readOnly = readOnly;
			tmp.selectionColor = selectionColor;
			tmp.shouldHideMobileInput = shouldHideMobileInput;
			tmp.text = _text;

			( (RectTransform) tmp.transform ).sizeDelta = sizeDelta;

			// Paste UnityEvents
			PasteUnityEvent( tmp.onEndEdit, onEndEdit );
			PasteUnityEvent( tmp.onValueChanged, onValueChanged );

			// TMP InputField objects have an extra Viewport (Text Area) child object, create it if necessary
			if( textComponent )
			{
				RectTransform viewport;
				if( textComponent.transform.parent != tmp.transform )
					viewport = (RectTransform) textComponent.transform.parent;
				else
					viewport = CreateInputFieldViewport( tmp, textComponent, placeholderComponent );

				if( !viewport.GetComponent<RectMask2D>() )
					viewport.gameObject.AddComponent<RectMask2D>();

				tmp.textViewport = viewport;
			}

			return tmp;
		}

		private TMP_Dropdown UpgradeDropdown( Dropdown dropdown )
		{
			if( !dropdown )
				return null;

			GameObject go = dropdown.gameObject;
			stringBuilder.AppendLine( "Upgrading Dropdown: " + GetPathOfObject( go.transform ) );

			// Copy fields
			Vector2 sizeDelta = ( (RectTransform) dropdown.transform ).sizeDelta;
			SelectableObjectProperties selectableProperties = new SelectableObjectProperties( dropdown );

			Image captionImage = dropdown.captionImage;
			bool enabled = dropdown.enabled;
			Image itemImage = dropdown.itemImage;
			List<TMP_Dropdown.OptionData> options = GetTMPDropdownOptions( dropdown.options );
			RectTransform template = dropdown.template;
			int value = dropdown.value;

			// Copy UnityEvents
			object onValueChanged = CopyUnityEvent( dropdown.onValueChanged );

			// Upgrade&copy child objects
			TextMeshProUGUI captionText = UpgradeText( dropdown.captionText );
			TextMeshProUGUI itemText = UpgradeText( dropdown.itemText );

			// Replace Dropdown with TMP_Dropdown
			DestroyImmediate( dropdown, true );
			TMP_Dropdown tmp = go.AddComponent<TMP_Dropdown>();

			// Paste child objects
			tmp.captionText = captionText;
			tmp.itemText = itemText;

			// Paste fields
			selectableProperties.ApplyTo( tmp );

			tmp.captionImage = captionImage;
			tmp.enabled = enabled;
			tmp.itemImage = itemImage;
			tmp.options = options;
			tmp.template = template;
			tmp.value = value;

			( (RectTransform) tmp.transform ).sizeDelta = sizeDelta;

			// Paste UnityEvents
			PasteUnityEvent( tmp.onValueChanged, onValueChanged );

			return tmp;
		}

		private RectTransform CreateInputFieldViewport( TMP_InputField tmp, TextMeshProUGUI textComponent, Graphic placeholderComponent )
		{
			RectTransform viewport = null;
			try
			{
				viewport = (RectTransform) new GameObject( "Text Area", typeof( RectTransform ) ).transform;
				viewport.transform.SetParent( tmp.transform, false );
				viewport.SetSiblingIndex( textComponent.rectTransform.GetSiblingIndex() );
				viewport.localPosition = textComponent.rectTransform.localPosition;
				viewport.localRotation = textComponent.rectTransform.localRotation;
				viewport.localScale = textComponent.rectTransform.localScale;
				viewport.anchorMin = textComponent.rectTransform.anchorMin;
				viewport.anchorMax = textComponent.rectTransform.anchorMax;
				viewport.pivot = textComponent.rectTransform.pivot;
				viewport.anchoredPosition = textComponent.rectTransform.anchoredPosition;
				viewport.sizeDelta = textComponent.rectTransform.sizeDelta;

#if UNITY_2018_3_OR_NEWER
				PrefabUtility.RecordPrefabInstancePropertyModifications( viewport.gameObject );
				PrefabUtility.RecordPrefabInstancePropertyModifications( viewport.transform );
#endif

				for( int i = tmp.transform.childCount - 1; i >= 0; i-- )
				{
					Transform child = tmp.transform.GetChild( i );
					if( child == viewport )
						continue;

					if( child == textComponent.rectTransform || ( placeholderComponent && child == placeholderComponent.rectTransform ) )
					{
						child.SetParent( viewport, true );
						child.SetSiblingIndex( 0 );

#if UNITY_2018_3_OR_NEWER
						PrefabUtility.RecordPrefabInstancePropertyModifications( child );
#endif
					}
				}
			}
			catch
			{
				if( viewport )
				{
					DestroyImmediate( viewport );
					viewport = null;
				}

				throw;
			}

			return viewport;
		}

		private List<TMP_Dropdown.OptionData> GetTMPDropdownOptions( List<Dropdown.OptionData> options )
		{
			if( options == null )
				return null;

			List<TMP_Dropdown.OptionData> result = new List<TMP_Dropdown.OptionData>( options.Count );
			for( int i = 0; i < options.Count; i++ )
				result.Add( new TMP_Dropdown.OptionData( options[i].text, options[i].image ) );

			return result;
		}

		private TMP_InputField.CharacterValidation GetTMPCharacterValidation( InputField.CharacterValidation characterValidation )
		{
			switch( characterValidation )
			{
				case InputField.CharacterValidation.Alphanumeric: return TMP_InputField.CharacterValidation.Alphanumeric;
				case InputField.CharacterValidation.Decimal: return TMP_InputField.CharacterValidation.Decimal;
				case InputField.CharacterValidation.EmailAddress: return TMP_InputField.CharacterValidation.EmailAddress;
				case InputField.CharacterValidation.Integer: return TMP_InputField.CharacterValidation.Integer;
				case InputField.CharacterValidation.Name: return TMP_InputField.CharacterValidation.Name;
				case InputField.CharacterValidation.None: return TMP_InputField.CharacterValidation.None;
				default: return TMP_InputField.CharacterValidation.None;
			}
		}

		private TMP_InputField.ContentType GetTMPContentType( InputField.ContentType contentType )
		{
			switch( contentType )
			{
				case InputField.ContentType.Alphanumeric: return TMP_InputField.ContentType.Alphanumeric;
				case InputField.ContentType.Autocorrected: return TMP_InputField.ContentType.Autocorrected;
				case InputField.ContentType.Custom: return TMP_InputField.ContentType.Custom;
				case InputField.ContentType.DecimalNumber: return TMP_InputField.ContentType.DecimalNumber;
				case InputField.ContentType.EmailAddress: return TMP_InputField.ContentType.EmailAddress;
				case InputField.ContentType.IntegerNumber: return TMP_InputField.ContentType.IntegerNumber;
				case InputField.ContentType.Name: return TMP_InputField.ContentType.Name;
				case InputField.ContentType.Password: return TMP_InputField.ContentType.Password;
				case InputField.ContentType.Pin: return TMP_InputField.ContentType.Pin;
				case InputField.ContentType.Standard: return TMP_InputField.ContentType.Standard;
				default: return TMP_InputField.ContentType.Standard;
			}
		}

		private TMP_InputField.InputType GetTMPInputType( InputField.InputType inputType )
		{
			switch( inputType )
			{
				case InputField.InputType.AutoCorrect: return TMP_InputField.InputType.AutoCorrect;
				case InputField.InputType.Password: return TMP_InputField.InputType.Password;
				case InputField.InputType.Standard: return TMP_InputField.InputType.Standard;
				default: return TMP_InputField.InputType.Standard;
			}
		}

		private TMP_InputField.LineType GetTMPLineType( InputField.LineType lineType )
		{
			switch( lineType )
			{
				case InputField.LineType.MultiLineNewline: return TMP_InputField.LineType.MultiLineNewline;
				case InputField.LineType.MultiLineSubmit: return TMP_InputField.LineType.MultiLineSubmit;
				case InputField.LineType.SingleLine: return TMP_InputField.LineType.SingleLine;
				default: return TMP_InputField.LineType.SingleLine;
			}
		}

		private TextAlignmentOptions GetTMPAlignment( TextAnchor alignment, bool alignByGeometry )
		{
			switch( alignment )
			{
				case TextAnchor.LowerLeft: return alignByGeometry ? TextAlignmentOptions.BottomLeft : TextAlignmentOptions.BottomLeft;
				case TextAnchor.LowerCenter: return alignByGeometry ? TextAlignmentOptions.BottomGeoAligned : TextAlignmentOptions.Bottom;
				case TextAnchor.LowerRight: return alignByGeometry ? TextAlignmentOptions.BottomRight : TextAlignmentOptions.BottomRight;
				case TextAnchor.MiddleLeft: return alignByGeometry ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Left;
				case TextAnchor.MiddleCenter: return alignByGeometry ? TextAlignmentOptions.MidlineGeoAligned : TextAlignmentOptions.Center;
				case TextAnchor.MiddleRight: return alignByGeometry ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.Right;
				case TextAnchor.UpperLeft: return alignByGeometry ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.TopLeft;
				case TextAnchor.UpperCenter: return alignByGeometry ? TextAlignmentOptions.TopGeoAligned : TextAlignmentOptions.Top;
				case TextAnchor.UpperRight: return alignByGeometry ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopRight;
				default: return alignByGeometry ? TextAlignmentOptions.MidlineGeoAligned : TextAlignmentOptions.Center;
			}
		}

		private FontStyles GetTMPFontStyle( FontStyle fontStyle )
		{
			switch( fontStyle )
			{
				case FontStyle.Bold: return FontStyles.Bold;
				case FontStyle.Italic: return FontStyles.Italic;
				case FontStyle.BoldAndItalic: return FontStyles.Bold | FontStyles.Italic;
				default: return FontStyles.Normal;
			}
		}

		private TextOverflowModes GetTMPVerticalOverflow( VerticalWrapMode verticalOverflow, HorizontalWrapMode horizontalOverflow )
		{
			if( alwaysUseOverflowForNonWrappingTexts && horizontalOverflow == HorizontalWrapMode.Overflow )
				return TextOverflowModes.Overflow;

			return verticalOverflow == VerticalWrapMode.Overflow ? TextOverflowModes.Overflow : TextOverflowModes.Truncate;
		}

		private object CopyUnityEvent( UnityEventBase target )
		{
			if( unityEventPersistentCallsField == null )
			{
				unityEventPersistentCallsField = typeof( UnityEventBase ).GetField( "m_PersistentCalls", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance );
				if( unityEventPersistentCallsField == null )
				{
					unityEventPersistentCallsField = typeof( UnityEventBase ).GetField( "m_PersistentListeners", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance );

					if( unityEventPersistentCallsField == null )
					{
						stringBuilder.AppendLine( "<b>Couldn't copy UnityEvent!</b>" );
						return null;
					}
				}
			}

			return unityEventPersistentCallsField.GetValue( target );
		}

		private void PasteUnityEvent( UnityEventBase target, object unityEvent )
		{
			unityEventPersistentCallsField.SetValue( target, unityEvent );
		}
	}
}