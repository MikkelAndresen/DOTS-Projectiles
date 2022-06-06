using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.Mathematics;

[GenerateAuthoringComponent]
public struct ProjectileFlightComponent : IComponentData
{
	public float3 startPosition;
	public float3 initialVel;
	public float time;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float3 ProjectilePositionAtTime(
			float3 initialVelocity, float time, float gtSq) => new float3(
		initialVelocity.x * time,
		initialVelocity.y * time - gtSq,
		initialVelocity.z * time);
}