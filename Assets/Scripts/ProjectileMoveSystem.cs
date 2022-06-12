using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;
using BoxCollider = Unity.Physics.BoxCollider;

public struct DeadComponentTag : IComponentData { }

[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(CopyInitialTransformFromGameObjectSystem))]
public partial class ProjectileMoveSystem : SystemBase
{
	protected override void OnStartRunning()
	{
		base.OnStartRunning();
		this.RegisterPhysicsRuntimeSystemReadOnly();
	}

	// TODO We may want to utilize a custom component to cache a two frame old position value,
	// apparently, (according to PhysicsStep component), collision query data may be delayed by a frame
	// and so that may result in tunneling.
	protected override void OnUpdate()
	{
		float dt = Time.DeltaTime;
		float g = -Physics.gravity.y;

		Entities.WithBurst().ForEach((Entity ent,
		   ref Translation translate,
		   ref Rotation rotation,
		   ref PhysicsCollider collider,
		   ref ProjectileFlightComponent flightComponent) =>
	   {
		   float3 prevPos = translate.Value;

		   flightComponent.time += dt;
		   float gtSq = g * flightComponent.time * flightComponent.time;
		   float3 newPos = flightComponent.startPosition +
				ProjectileFlightComponent.ProjectilePositionAtTime(flightComponent.initialVel, flightComponent.time, gtSq);

		   float3 direction = newPos - prevPos;
		   float directionMagnitude = math.length(direction);
		   quaternion rot = quaternion.LookRotationSafe(direction, math.up());

		   // We set the box collider to be stretched between the last and current positions.
		   // TODO See if raycasts are cheaper than relying on box colliders.
		   BoxGeometry box = new BoxGeometry
		   {
			   Orientation = rot,
			   // TODO Add a min size because we want to encapsulate the projectile
			   Size = new float3(1, 1, directionMagnitude),
			   Center = new float3(0, 0, -directionMagnitude * 0.5f)
		   };

		   collider.Value = BoxCollider.Create(box);

		   translate.Value = newPos;
		   rotation.Value = rot;
	   }).ScheduleParallel();
	}
}