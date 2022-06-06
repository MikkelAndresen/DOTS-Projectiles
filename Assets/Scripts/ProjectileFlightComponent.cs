using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.Mathematics;

[GenerateAuthoringComponent]
public struct ProjectileFlightComponent : IComponentData/*, IConvertGameObjectToEntity*/
{
	public float3 startPosition;
	public float3 initialVel;
	public float time;

	//public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
	//{
	//	dstManager.AddComponent(entity, typeof(ProjectileFlightComponent));
	//	dstManager.SetComponentData(entity, new ProjectileFlightComponent() { startPosition = })
	//}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float3 ProjectilePositionAtTime(
			float3 initialVelocity, float time, float gtSq) => new float3(
		initialVelocity.x * time,
		initialVelocity.y * time - gtSq,
		initialVelocity.z * time);
}