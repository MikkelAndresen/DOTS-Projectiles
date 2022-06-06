using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Authoring;
using Unity.Physics.Systems;
using Debug = UnityEngine.Debug;
using Resources = UnityEngine.Resources;

public abstract partial class PhysicsEventSystemBase : SystemBase
{
	protected BuildPhysicsWorld BuildPhysicsWorld;
	protected StepPhysicsWorld StepPhysicsWorld;
	protected EntityCommandBufferSystem CommandBufferSystem;

	protected override void OnCreate()
	{
		BuildPhysicsWorld = World.GetOrCreateSystem<BuildPhysicsWorld>();
		StepPhysicsWorld = World.GetOrCreateSystem<StepPhysicsWorld>();
		CommandBufferSystem = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
	}
}

/// <summary>
/// This class is just utility for loading template scriptable objects made using the editor.
/// It can also be used to create box collisions based on these templates.
/// <see cref="GetProjectileBoxWCollision(BoxGeometry)"/> and <see cref="GetProjectileBoxWoCollision(BoxGeometry)"/>
/// </summary>
public static class ProjectileProperties
{
	private static PhysicsMaterialTemplate projectileCollideTemplate;
	public static PhysicsMaterialTemplate ProjectileCollideTemplate => projectileCollideTemplate == null ?
		projectileCollideTemplate = Resources.Load<PhysicsMaterialTemplate>("ProjectileCollide") : projectileCollideTemplate;

	public static readonly CollisionFilter CollideFilter = new CollisionFilter()
	{
		BelongsTo = ProjectileCollideTemplate.BelongsTo.Value,
		CollidesWith = ProjectileCollideTemplate.CollidesWith.Value,
		GroupIndex = 0
	};
	public static readonly Material CollideMaterial = new Material()
	{
		CollisionResponse = ProjectileCollideTemplate.CollisionResponse,
		CustomTags = ProjectileCollideTemplate.CustomTags.Value,
		Friction = ProjectileCollideTemplate.Friction.Value,
		Restitution = ProjectileCollideTemplate.Restitution.Value
	};

	private static PhysicsMaterialTemplate projectileNoCollideTemplate;
	public static PhysicsMaterialTemplate ProjectileNoCollideTemplate => projectileNoCollideTemplate == null ?
		projectileNoCollideTemplate = Resources.Load<PhysicsMaterialTemplate>("ProjectileNoCollide") : projectileNoCollideTemplate;

	public static readonly CollisionFilter NoCollideFilter = new CollisionFilter()
	{
		BelongsTo = ProjectileNoCollideTemplate.BelongsTo.Value,
		CollidesWith = ProjectileNoCollideTemplate.CollidesWith.Value,
		GroupIndex = 0
	};
	public static readonly Material NoCollideMaterial = new Material()
	{
		CollisionResponse = ProjectileNoCollideTemplate.CollisionResponse,
		CustomTags = ProjectileNoCollideTemplate.CustomTags.Value,
		Friction = ProjectileNoCollideTemplate.Friction.Value,
		Restitution = ProjectileNoCollideTemplate.Restitution.Value
	};

	public static BlobAssetReference<Collider> GetProjectileBoxWCollision(BoxGeometry geometry) =>
		BoxCollider.Create(geometry, CollideFilter, CollideMaterial);
	public static BlobAssetReference<Collider> GetProjectileBoxWoCollision(BoxGeometry geometry) =>
		BoxCollider.Create(geometry, NoCollideFilter, NoCollideMaterial);
}


// This system sets the PhysicsGravityFactor of any dynamic body that enters a Trigger Volume.
// A Trigger Volume is defined by a PhysicsShapeAuthoring with the `Is Trigger` flag ticked and a
// TriggerGravityFactor behaviour added.
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(EndFramePhysicsSystem))]
[AlwaysUpdateSystem] // Required unless you use an entity query in the update loop
public partial class PlayerTriggerSystem : PhysicsEventSystemBase
{
	private static EndSimulationEntityCommandBufferSystem endSimBufferSys;

	public static readonly CollisionFilter CollideFilter = ProjectileProperties.CollideFilter;
	public static readonly Material CollideMaterial = ProjectileProperties.CollideMaterial;
	public static readonly CollisionFilter NoCollideFilter = ProjectileProperties.NoCollideFilter;
	public static readonly Material NoCollideMaterial = ProjectileProperties.NoCollideMaterial;

	protected override void OnCreate()
	{
		base.OnCreate();
		endSimBufferSys = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();

		if (!ShouldRunSystem())
			Debug.LogError("Error: Projectile trigger system not running");
	}

	protected override void OnStartRunning()
	{
		base.OnStartRunning();
		this.RegisterPhysicsRuntimeSystemReadOnly();
	}

	private JobHandle job;
	protected override void OnUpdate()
	{
		job = new PlayerTriggerJob(
			ecb: endSimBufferSys.CreateCommandBuffer(),
			playerGroup: GetComponentDataFromEntity<PlayerTagComponent>(true),
			projectileGroup: GetComponentDataFromEntity<ProjectileTagComponent>(true),
			ProjectileProperties.NoCollideFilter,
			ProjectileProperties.NoCollideMaterial).
			Schedule(StepPhysicsWorld.Simulation, Dependency);

		Dependency = job;
		
		endSimBufferSys.AddJobHandleForProducer(job);
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();
		//deadEnts.Dispose();
	}

	[BurstCompile]
	public struct PlayerTriggerJob : ITriggerEventsJob
	{
		[ReadOnly] private ComponentDataFromEntity<PlayerTagComponent> playerGroup;
		[ReadOnly] private ComponentDataFromEntity<ProjectileTagComponent> projectileGroup;
		[ReadOnly] private CollisionFilter noCollideFilter;
		[ReadOnly] private Material noCollideMaterial;
		private EntityCommandBuffer ecb;
		
		public PlayerTriggerJob(
			EntityCommandBuffer ecb,
			ComponentDataFromEntity<PlayerTagComponent> playerGroup,
			ComponentDataFromEntity<ProjectileTagComponent> projectileGroup,
			CollisionFilter noCollideFilter,
			Material noCollideMaterial)
		{
			this.ecb = ecb;
			this.playerGroup = playerGroup;
			this.projectileGroup = projectileGroup;
			this.noCollideFilter = noCollideFilter;
			this.noCollideMaterial = noCollideMaterial;
		}

		public void Execute(TriggerEvent triggerEvent)
		{
			//Debug.Log($"triggered {triggerEvent.EntityA.Index} & {triggerEvent.EntityB.Index}");
			Entity entityA = triggerEvent.EntityA;
			Entity entityB = triggerEvent.EntityB;
			bool isAPlayer = playerGroup.HasComponent(entityA);
			bool isBPlayer = playerGroup.HasComponent(entityB);

			// Either all or no players is invalid for our case
			if ((isAPlayer && isBPlayer) || (!isAPlayer && !isBPlayer))
				return;

			// We need one projectile
			bool hasProj = isAPlayer ? projectileGroup.HasComponent(entityB) : projectileGroup.HasComponent(entityA);
			if (!hasProj)
				return;

			if (isAPlayer)
			{
				//ecb.SetComponent<PhysicsCollider>(entityA, BoxCollider.Create());
				//ecb.AddComponent<DeadComponentTag>(entityA);
				ecb.RemoveComponent<ProjectileFlightComponent>(entityB);
				RemoveCollisionOnEntity(entityB, ecb, noCollideFilter, noCollideMaterial);
			}
			else
			{
				//ecb.AddComponent<DeadComponentTag>(entityB);
				// TODO Try setting should playback on ecb based on whether or not there are > 0 collisions
				ecb.RemoveComponent<ProjectileFlightComponent>(entityA);
				RemoveCollisionOnEntity(entityA, ecb, noCollideFilter, noCollideMaterial);
			}

			// We can use this method to stop collision between the projectiles and the world,
			// that way we can prevent getting > 1 trigger callback since we want the projectile to stop moving.
			static void RemoveCollisionOnEntity(Entity ent, EntityCommandBuffer ecb, CollisionFilter filter, Material mat)
			{
				PhysicsCollider col = new PhysicsCollider
				{
					Value = BoxCollider.Create(new BoxGeometry()
					{
						Center = float3.zero,
						BevelRadius = 0.05f,
						Orientation = quaternion.identity,
						Size = new float3(0.5f, 0.5f, 1f)
					}, filter, mat)

				};
				ecb.SetComponent(ent, col);
			}
		}
	}
}