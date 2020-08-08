using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TextToTMPNamespace
{
	public partial class TextToTMPWindow
	{
		#region Helper Classes
		[Serializable]
		private class PendingReferenceUpdate
		{
			public enum TargetType { Text, InputField, Dropdown, TextMesh, Font }

			public Object source;
			public string sourcePath;
			public string sourceType;
			public string[] propertyPaths;
			public Object[] targets;
			public TargetType[] targetTypes;
			public string[] targetPaths;
		}
		#endregion

		// Unity's internal function that returns a SerializedProperty's corresponding FieldInfo
		private delegate FieldInfo FieldInfoGetter( SerializedProperty p, out Type t );
		private FieldInfoGetter fieldInfoGetter;

		private List<PendingReferenceUpdate> pendingReferenceUpdates = new List<PendingReferenceUpdate>();

		private void CollectReferences()
		{
#if UNITY_2019_3_OR_NEWER
			MethodInfo fieldInfoGetterMethod = typeof( Editor ).Assembly.GetType( "UnityEditor.ScriptAttributeUtility" ).GetMethod( "GetFieldInfoAndStaticTypeFromProperty", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static );
#else
			MethodInfo fieldInfoGetterMethod = typeof( Editor ).Assembly.GetType( "UnityEditor.ScriptAttributeUtility" ).GetMethod( "GetFieldInfoFromProperty", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static );
#endif
			fieldInfoGetter = (FieldInfoGetter) Delegate.CreateDelegate( typeof( FieldInfoGetter ), fieldInfoGetterMethod );

			pendingReferenceUpdates.Clear();

			int progressCurrent = 0;
			int progressTotal = assetsToUpgrade.EnabledCount + scenesToUpgrade.EnabledCount;
			try
			{
				foreach( string asset in assetsToUpgrade )
				{
					EditorUtility.DisplayProgressBar( "Collecting references from assets...", asset, (float) progressCurrent / progressTotal );
					progressCurrent++;

					Object[] assetsAtPath = AssetDatabase.LoadAllAssetsAtPath( asset );
					for( int i = 0; i < assetsAtPath.Length; i++ )
						CollectReferencesFromObject( assetsAtPath[i] );
				}

				foreach( string scene in scenesToUpgrade )
				{
					EditorUtility.DisplayProgressBar( "Collecting references from scenes...", scene, (float) progressCurrent / progressTotal );
					progressCurrent++;

					CollectReferencesInScene( SceneManager.GetSceneByPath( scene ) );
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
		}

		private void UpdateReferences()
		{
			stringBuilder.Length = 0;

			int progressCurrent = 0;
			int progressTotal = pendingReferenceUpdates.Count;
			try
			{
				foreach( PendingReferenceUpdate reference in pendingReferenceUpdates )
				{
					EditorUtility.DisplayProgressBar( "Updating references...", reference.source ? reference.source.name : "Object", (float) progressCurrent / progressTotal );
					progressCurrent++;

					if( !reference.source )
					{
						reference.source = ConvertReferencePathToObject( reference.sourcePath, Type.GetType( reference.sourceType ) );
						if( !reference.source )
						{
							stringBuilder.AppendLine( "<b>Pending reference source is no longer valid: " + reference.sourcePath + "</b>" );
							continue;
						}
					}

					SerializedObject so = new SerializedObject( reference.source );

					for( int j = 0; j < reference.propertyPaths.Length; j++ )
					{
						Object target = reference.targets[j];
						if( !target )
						{
							target = ConvertReferencePathToObject( reference.targetPaths[j], reference.targetTypes[j] == PendingReferenceUpdate.TargetType.Font ? typeof( Font ) : typeof( GameObject ) );
							if( !target )
							{
								stringBuilder.AppendLine( "<b>Pending reference target (at path " + reference.targetPaths[j] + ") is no longer valid for: " + reference.source.name + " (" + reference.source.GetType().Name + ") -> " + reference.propertyPaths[j] + "</b>" );
								continue;
							}
						}

						switch( reference.targetTypes[j] )
						{
							case PendingReferenceUpdate.TargetType.Text: target = ( (GameObject) target ).GetComponent<TextMeshProUGUI>(); break;
							case PendingReferenceUpdate.TargetType.InputField: target = ( (GameObject) target ).GetComponent<TMP_InputField>(); break;
							case PendingReferenceUpdate.TargetType.Dropdown: target = ( (GameObject) target ).GetComponent<TMP_Dropdown>(); break;
							case PendingReferenceUpdate.TargetType.TextMesh: target = ( (GameObject) target ).GetComponent<TextMeshPro>(); break;
							case PendingReferenceUpdate.TargetType.Font:
								stringBuilder.AppendLine( "Changing Font variable to TMP Font: " + reference.source.name + " (" + reference.source.GetType().Name + ") -> " + reference.propertyPaths[j] );
								target = GetCorrespondingTMPFontAsset( (Font) target );

								break;
						}

						if( !target ) // Component wasn't upgraded
							continue;

						SerializedProperty property = so.FindProperty( reference.propertyPaths[j] );
						if( property.propertyType == SerializedPropertyType.ObjectReference )
							property.objectReferenceValue = target;
						else if( property.propertyType == SerializedPropertyType.ExposedReference )
							property.exposedReferenceValue = target;

						if( reference.targetTypes[j] != PendingReferenceUpdate.TargetType.Font )
						{
							if( ( property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == target ) ||
								( property.propertyType == SerializedPropertyType.ExposedReference && property.exposedReferenceValue == target ) )
							{
								stringBuilder.AppendLine( "Updated reference: " + reference.source.name + " (" + reference.source.GetType().Name + ") -> " + reference.propertyPaths[j] );
							}
						}
					}

					so.ApplyModifiedPropertiesWithoutUndo();
				}

				EditorSceneManager.MarkAllScenesDirty();
			}
			finally
			{
				if( stringBuilder.Length > 0 )
				{
					stringBuilder.Insert( 0, "<b>Reconnect References Logs:</b>\n" );
					Debug.Log( stringBuilder.ToString() );
				}

				EditorUtility.ClearProgressBar();

				AssetDatabase.SaveAssets();
				EditorSceneManager.SaveOpenScenes();
			}
		}

		private void CollectReferencesInScene( Scene scene )
		{
			if( !scene.IsValid() )
			{
				Debug.LogError( "Scene " + scene.name + " is not valid, can't collect references inside it!" );
				return;
			}

			if( !scene.isLoaded )
			{
				Debug.LogError( "Scene " + scene.name + " is not loaded, can't collect references inside it!" );
				return;
			}

			GameObject[] rootGameObjects = scene.GetRootGameObjects();
			for( int i = 0; i < rootGameObjects.Length; i++ )
			{
				Component[] components = rootGameObjects[i].GetComponentsInChildren<Component>( true );
				for( int j = 0; j < components.Length; j++ )
					CollectReferencesFromObject( components[j] );
			}
		}

		private void CollectReferencesFromObject( Object obj )
		{
			if( !obj )
				return;

			if( !( obj is Component ) && !( obj is ScriptableObject ) )
				return;

			if( ( obj.hideFlags & HideFlags.NotEditable ) == HideFlags.NotEditable )
				return;

			// These components will be upgraded and be destroyed in the process
			if( obj is Text || obj is InputField || obj is Dropdown || obj is TextMesh )
				return;

			if( obj is MonoBehaviour )
				AddScriptToUpgrade( AssetDatabase.GetAssetPath( MonoScript.FromMonoBehaviour( (MonoBehaviour) obj ) ) );
			else if( obj is ScriptableObject )
				AddScriptToUpgrade( AssetDatabase.GetAssetPath( MonoScript.FromScriptableObject( (ScriptableObject) obj ) ) );

			SerializedObject so = new SerializedObject( obj );
			SerializedProperty iterator = so.GetIterator();
			SerializedProperty iteratorVisible = so.GetIterator();
			if( iterator.Next( true ) )
			{
				List<string> propertyPaths = new List<string>( 0 );
				List<Object> targets = new List<Object>( 0 );
				List<PendingReferenceUpdate.TargetType> targetTypes = new List<PendingReferenceUpdate.TargetType>( 0 );
				List<string> targetPaths = new List<string>( 0 );

				bool iteratingVisible = iteratorVisible.NextVisible( true );
				bool enterChildren;
				do
				{
					// Iterate over NextVisible properties AND the properties that have corresponding FieldInfos (internal Unity
					// properties don't have FieldInfos so we are skipping them, which is good because search results found in
					// those properties aren't interesting and mostly confusing)
					bool isVisible = iteratingVisible && SerializedProperty.EqualContents( iterator, iteratorVisible );
					if( isVisible )
						iteratingVisible = iteratorVisible.NextVisible( true );
					else
					{
						Type propFieldType;
						isVisible = iterator.type == "Array" || fieldInfoGetter( iterator, out propFieldType ) != null;
					}

					if( !isVisible )
					{
						enterChildren = false;
						continue;
					}

					Object value = null;
					switch( iterator.propertyType )
					{
						case SerializedPropertyType.ObjectReference:
							value = iterator.objectReferenceValue;
							enterChildren = false;
							break;
						case SerializedPropertyType.ExposedReference:
							value = iterator.exposedReferenceValue;
							enterChildren = false;
							break;
#if UNITY_2019_3_OR_NEWER
						case SerializedPropertyType.ManagedReference:
							enterChildren = false;
							break;
#endif
						case SerializedPropertyType.Generic:
							enterChildren = true;
							break;
						default:
							enterChildren = false;
							break;
					}

					if( value )
					{
						if( value is Font )
						{
							propertyPaths.Add( iterator.propertyPath );
							targets.Add( value );
							targetTypes.Add( PendingReferenceUpdate.TargetType.Font );
							targetPaths.Add( ConvertObjectToReferencePath( value ) );
						}
						else if( value is Text )
						{
							if( ShouldCollectReference( value ) )
							{
								propertyPaths.Add( iterator.propertyPath );
								targets.Add( ( (Text) value ).gameObject );
								targetTypes.Add( PendingReferenceUpdate.TargetType.Text );
								targetPaths.Add( ConvertObjectToReferencePath( value ) );
							}
						}
						else if( value is InputField )
						{
							if( ShouldCollectReference( value ) )
							{
								propertyPaths.Add( iterator.propertyPath );
								targets.Add( ( (InputField) value ).gameObject );
								targetTypes.Add( PendingReferenceUpdate.TargetType.InputField );
								targetPaths.Add( ConvertObjectToReferencePath( value ) );
							}
						}
						else if( value is Dropdown )
						{
							if( ShouldCollectReference( value ) )
							{
								propertyPaths.Add( iterator.propertyPath );
								targets.Add( ( (Dropdown) value ).gameObject );
								targetTypes.Add( PendingReferenceUpdate.TargetType.Dropdown );
								targetPaths.Add( ConvertObjectToReferencePath( value ) );
							}
						}
						else if( value is TextMesh )
						{
							if( ShouldCollectReference( value ) )
							{
								propertyPaths.Add( iterator.propertyPath );
								targets.Add( ( (TextMesh) value ).gameObject );
								targetTypes.Add( PendingReferenceUpdate.TargetType.TextMesh );
								targetPaths.Add( ConvertObjectToReferencePath( value ) );
							}
						}
					}
				} while( iterator.Next( enterChildren ) );

				if( propertyPaths.Count > 0 )
				{
					pendingReferenceUpdates.Add( new PendingReferenceUpdate()
					{
						source = obj,
						sourcePath = ConvertObjectToReferencePath( obj ),
						sourceType = obj.GetType().AssemblyQualifiedName,
						propertyPaths = propertyPaths.ToArray(),
						targets = targets.ToArray(),
						targetTypes = targetTypes.ToArray(),
						targetPaths = targetPaths.ToArray()
					} );
				}
			}
		}

		private bool ShouldCollectReference( Object target )
		{
			string assetPath = AssetDatabase.GetAssetOrScenePath( target );
			return !string.IsNullOrEmpty( assetPath ) && ( assetsToUpgrade.Contains( assetPath ) || scenesToUpgrade.Contains( assetPath ) );
		}

		// Returns a path that can later be used to find the target Object again
		private string ConvertObjectToReferencePath( Object target )
		{
			if( !target )
				return null;

			string assetPath = AssetDatabase.GetAssetOrScenePath( target );
			if( string.IsNullOrEmpty( assetPath ) )
				return null;

			assetPath += "<>"; // A unique separator

			if( target is GameObject )
				assetPath += GetPathOfObject( ( (GameObject) target ).transform );
			else if( target is Component )
				assetPath += GetPathOfObject( ( (Component) target ).transform );

			return assetPath;
		}

		// If a reference became null during the upgrade process, this functions tries to restore that reference
		private Object ConvertReferencePathToObject( string referencePath, Type referenceType )
		{
			stringBuilder.AppendLine( "Reference to " + referencePath + " was lost during the upgrade. Attempting to restore it." );

			if( string.IsNullOrEmpty( referencePath ) )
			{
				stringBuilder.AppendLine( "Couldn't restore reference: referencePath was null" );
				return null;
			}

			if( referenceType == null )
			{
				stringBuilder.AppendLine( "Couldn't restore reference: referenceType was null" );
				return null;
			}

			int pathSplitIndex = referencePath.IndexOf( "<>" );
			if( pathSplitIndex < 0 )
			{
				stringBuilder.AppendLine( "Couldn't restore reference: referencePath didn't have '<>' separator" );
				return null;
			}

			string assetOrScenePath = referencePath.Substring( 0, pathSplitIndex );
			string hierarchyPath = referencePath.Length > pathSplitIndex + 2 ? referencePath.Substring( pathSplitIndex + 2 ) : null;

			if( referenceType == typeof( Font ) )
				return AssetDatabase.LoadAssetAtPath<Font>( assetOrScenePath );
			else if( referenceType == typeof( ScriptableObject ) )
				return AssetDatabase.LoadAssetAtPath<ScriptableObject>( assetOrScenePath );
			else if( !typeof( Component ).IsAssignableFrom( referenceType ) && !typeof( GameObject ).IsAssignableFrom( referenceType ) )
			{
				stringBuilder.AppendLine( "Couldn't restore reference: referenceType was extending " + referenceType.FullName );
				return null;
			}

			Object rootAsset = AssetDatabase.LoadMainAssetAtPath( assetOrScenePath );
			if( !rootAsset )
			{
				stringBuilder.AppendLine( "Couldn't restore reference: Asset/Scene couldn't be loaded from referencePath" );
				return null;
			}

			if( string.IsNullOrEmpty( hierarchyPath ) )
			{
				stringBuilder.AppendLine( "Couldn't restore reference: hierarchyPath was null" );
				return null;
			}

			string[] pathComponents = hierarchyPath.Split( '/' );

			if( rootAsset is SceneAsset )
			{
				Scene scene = SceneManager.GetSceneByPath( assetOrScenePath );
				if( !scene.IsValid() || !scene.isLoaded )
				{
					stringBuilder.AppendLine( "Couldn't restore reference: Scene at referencePath wasn't valid or it wasn't loaded" );
					return null;
				}

				GameObject[] sceneRoot = scene.GetRootGameObjects();
				for( int i = 0; i < sceneRoot.Length; i++ )
				{
					Object result = FindObjectInHierarchyRecursively( sceneRoot[i].transform, pathComponents, 0, referenceType );
					if( result )
						return result;
				}
			}
			else
			{
				if( !( rootAsset is GameObject ) )
				{
					stringBuilder.AppendLine( "Couldn't restore reference: main asset at referencePath wasn't a GameObject, it was a " + rootAsset.GetType().FullName );
					return null;
				}

				return FindObjectInHierarchyRecursively( ( (GameObject) rootAsset ).transform, pathComponents, 0, referenceType );
			}

			return null;
		}

		private Object FindObjectInHierarchyRecursively( Transform obj, string[] path, int pathIndex, Type targetType )
		{
			if( obj.name != path[pathIndex] )
				return null;

			if( pathIndex == path.Length - 1 )
			{
				if( typeof( GameObject ).IsAssignableFrom( targetType ) )
					return obj.gameObject;
				else
					return obj.GetComponent( targetType );
			}

			for( int i = obj.childCount - 1; i >= 0; i-- )
			{
				Object result = FindObjectInHierarchyRecursively( obj.GetChild( i ), path, pathIndex + 1, targetType );
				if( result )
					return result;
			}

			// While InputFields are upgraded to TMP_InputField, a child object called "Text Area" can be created in the process. Then, InputField's child
			// objects are made children of this "Text Area". This can break reference paths, so we should enter "Text Area" manually in this case
			if( obj.GetComponent<TMP_InputField>() )
			{
				Transform inputFieldViewport = obj.Find( "Text Area" );
				if( inputFieldViewport )
				{
					for( int i = inputFieldViewport.childCount - 1; i >= 0; i-- )
					{
						Object result = FindObjectInHierarchyRecursively( inputFieldViewport.GetChild( i ), path, pathIndex + 1, targetType );
						if( result )
							return result;
					}
				}
			}

			return null;
		}
	}
}