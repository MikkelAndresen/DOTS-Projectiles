using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using Unity.Physics;
using UnityEngine.Profiling;
using Unity.Mathematics;
using Unity.Physics.Extensions;
using System;
using Unity.Transforms;
using Unity.Rendering.HybridV2;
using Unity.Rendering;
using UnityEngine.Rendering;
using Collider = Unity.Physics.Collider;
using BoxCollider = Unity.Physics.BoxCollider;
using Unity.Physics.Authoring;
using Unity.Physics.GraphicsIntegration;
using System.Linq;
using Unity.Profiling;
using Unity.Jobs.LowLevel.Unsafe;
using Random = Unity.Mathematics.Random;
using System.Threading;
using TMPro;

namespace ECSTesting
{
	public class ProjectileLauncher : MonoBehaviour
	{
		public ComponentType[] ArchetypeCollection { get; private set; }

		public static EntityArchetype Archetype { get; private set; }
		[SerializeField]
		private uint physicsTickPerSecond = 90;
		[SerializeField]
		private int projectileCountToFire = 1;
		[SerializeField]
		private int projectileCounter;
		[SerializeField]
		private TextMeshProUGUI projectileCounterText;
		private int ProjectileCounter
		{
			get => projectileCounter;
			set
			{
				projectileCounter = value;
				if (projectileCounterText != null)
					projectileCounterText.text = projectileCounter.ToString();
			}
		}

		[SerializeField]
		private RenderMesh renderData;
		[SerializeField]
		private float3 initialVel;
		[SerializeField]
		private PhysicsMaterialTemplate materialTemplate;
		[SerializeField]
		private GameObject prefab;
		public Entity PrefabEnt { get; private set; }
		private BlobAssetStore prefabBlob;

		private RenderMeshDescription renderDescription;
		private EntityManager Manager => World.DefaultGameObjectInjectionWorld.EntityManager;

		void Start()
		{
			if (renderData.mesh != null && renderData.material != null)
			{
				renderDescription = new RenderMeshDescription(
					renderData.mesh,
					renderData.material,
					renderData.castShadows,
					receiveShadows: renderData.receiveShadows,
					MotionVectorGenerationMode.Camera,
					layer: renderData.layer,
					subMeshIndex: renderData.subMesh);
			}

			if (prefab != null)
			{
				prefabBlob = new BlobAssetStore();
				GameObjectConversionSettings settings = GameObjectConversionSettings.FromWorld(World.DefaultGameObjectInjectionWorld, prefabBlob);
				PrefabEnt = GameObjectConversionUtility.ConvertGameObjectHierarchy(prefab, settings);
				//FireProjectile();
			}
			else
				CreateBody(Manager, renderData, transform.position, transform.rotation, GetCollider(), Vector3.zero, Vector3.zero, 1, false);

			var fixedStepGroup = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<FixedStepSimulationSystemGroup>();
			//fixedStepGroup.Timestep = 1f / 4f; // Set fixed step here, find out why collision don't happen at this rate
			//fixedStepGroup.Timestep = 1f / 15f; // Works very badly
			fixedStepGroup.Timestep = 1f / physicsTickPerSecond;

			// TODO We may want to add a PhysicsStep component so that we can change the thread count among other things

			//var stepPhysics = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<PhysicsStep>
			// Can improve performance, TODO test with android
			JobsUtility.JobWorkerCount = 4;
		}

		private void Update()
		{
			if (Input.GetKeyDown(KeyCode.Space))
				FireAllProjectiles();

			//Thread.Sleep(10);
		}

		private void OnDestroy()
		{
			if (prefabBlob != null)
				prefabBlob.Dispose();
		}

		public void FireAllProjectiles()
		{
			ProjectileCounter += projectileCountToFire;
			for (int i = 0; i < projectileCountToFire; i++)
				FireSingleProjectile();
		}

		private ProfilerMarker FireMarker = new ProfilerMarker(nameof(ProjectileLauncher) + "." + nameof(FireSingleProjectile));
		public void FireSingleProjectile()
		{
			Debug.Assert(PrefabEnt != Entity.Null);

			FireMarker.Begin();

			var proj = Manager.Instantiate(PrefabEnt);
			float3 pos = RandomPosition();
			quaternion rot = transform.rotation;
			Manager.SetComponentData(proj, new Translation { Value = pos });
			Manager.SetComponentData(proj, new Rotation { Value = rot });
			Manager.AddComponent(proj, typeof(ProjectileFlightComponent));
			Manager.SetComponentData(proj, new ProjectileFlightComponent() { startPosition = pos, initialVel = initialVel });
			FireMarker.End();
		}

		private static Random r = new Random(1851936439U);
		private static readonly float3 Min = new float3(-5, -5, 0);
		private static readonly float3 Max = new float3(5, 5, 0);
		private float3 RandomPosition() => ((float3)transform.position) + r.NextFloat3(Min, Max);

		#region Physics Entity Archetypes

		private readonly ComponentType[] physicsComponents;
		private ComponentType[] PhysicsComponents => physicsComponents ?? (new ComponentType[]
		{
			typeof(Translation),
			typeof(Rotation),
			typeof(WorldToLocal),
			typeof(LocalToWorld),
			typeof(WorldToLocal_Tag),
			typeof(PhysicsCollider),
			typeof(PhysicsWorldIndex),
		});

		private EntityArchetype physicsArchetype;
		private EntityArchetype PhysicsArchetype =>
			physicsArchetype.Valid ? physicsArchetype :
				World.DefaultGameObjectInjectionWorld.EntityManager.CreateArchetype(PhysicsComponents);

		private readonly ComponentType[] dynamicPhysicsComponents;
		private ComponentType[] DynamicPhysicsComponents => dynamicPhysicsComponents ?? (new ComponentType[]
		{
			typeof(PhysicsGraphicalSmoothing),
			typeof(PhysicsGraphicalInterpolationBuffer),
			typeof(PhysicsMass),
			typeof(PhysicsVelocity),
			typeof(PhysicsDamping),
			typeof(PhysicsGravityFactor),
		}.Concat(PhysicsComponents).ToArray());

		private EntityArchetype dynamicPhysicsArchetype;
		private EntityArchetype DynamicPhysicsArchetype =>
			dynamicPhysicsArchetype.Valid ? dynamicPhysicsArchetype :
				World.DefaultGameObjectInjectionWorld.EntityManager.CreateArchetype(DynamicPhysicsComponents);

		#endregion

		// Taken from https://docs.unity3d.com/Packages/com.unity.physics@0.50/manual/interacting_with_bodies.html
		public unsafe Entity CreateBody(
			EntityManager entMan,
			RenderMesh displayMesh, float3 position, quaternion orientation, BlobAssetReference<Collider> collider,
			float3 linearVelocity, float3 angularVelocity, float mass, bool isDynamic)
		{
			if (displayMesh.mesh == null)
				throw new ArgumentNullException(nameof(displayMesh));

			Entity entity = entMan.CreateEntity(isDynamic ? DynamicPhysicsArchetype : PhysicsArchetype);
			RenderMeshUtility.AddComponents(entity, entMan, in renderDescription);

			//entMan.SetSharedComponentData(entity, displayMesh);
			entMan.SetComponentData(entity, new RenderBounds { Value = displayMesh.mesh.bounds.ToAABB() });

			entMan.SetComponentData(entity, new Translation { Value = position });
			entMan.SetComponentData(entity, new Rotation { Value = orientation });

			entMan.SetComponentData(entity, new PhysicsCollider { Value = collider });

			if (isDynamic)
			{
				Collider* colliderPtr = (Collider*)collider.GetUnsafePtr();
				entMan.SetComponentData(entity, PhysicsMass.CreateDynamic(colliderPtr->MassProperties, mass));
				// Calculate the angular velocity in local space from rotation and world angular velocity
				float3 angularVelocityLocal = math.mul(math.inverse(colliderPtr->MassProperties.MassDistribution.Transform.rot), angularVelocity);
				entMan.SetComponentData(entity, new PhysicsVelocity()
				{
					Linear = linearVelocity,
					Angular = angularVelocityLocal
				});
				entMan.SetComponentData(entity, new PhysicsDamping()
				{
					Linear = 0.01f,
					Angular = 0.05f
				});
			}

			return entity;
		}

		private BlobAssetReference<Collider> GetCollider() => BoxCollider.Create(
			new BoxGeometry()
			{
				BevelRadius = 0,
				Center = transform.position,
				Orientation = transform.rotation,
				Size = transform.localScale
			},
			MaterialTemplateToFilter(materialTemplate),
			MaterialTemplateToMaterial(materialTemplate));

		public static CollisionFilter MaterialTemplateToFilter(PhysicsMaterialTemplate template) =>
			template != null ? new CollisionFilter()
			{
				BelongsTo = template.BelongsTo.Value,
				CollidesWith = template.CollidesWith.Value
			} : default;

		public static Unity.Physics.Material MaterialTemplateToMaterial(PhysicsMaterialTemplate template)
		{
			if (template == null)
				return default;

			return new Unity.Physics.Material()
			{
				CollisionResponse = template.CollisionResponse,
				CustomTags = template.CustomTags.Value,
				Restitution = template.Restitution.Value,
				Friction = template.Friction.Value
			};
		}
	}
}