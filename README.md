# DOTS-Projectiles
Simple projectile motion system made using Unity DOTS Physics.
This system was made to have decent collision detection between "players" and "projectiles" on Android with low tick rates and avoiding tunneling.

It contains two systems:
- ProjectileMoveSystem
	* Moves all entities with the ProjectileFlightComponent using a projectile motion function taking in initial velocity, start position and time.
	* This system also changes the BoxCollider on the entity to be stretched between the current and last position which enables us to check for trigger events on the movement path.
	That way we can turn the tick rate for these projectiles down because it should just keep checking the space between the frames.
	
- PlayerTriggerSystem
	* Runs a ITriggerEventsJob to receive the trigger callbacks between entities.
	* Checks if the triggered ents are players or projectiles, then changes the collision filter and removes the ProjectileFlightComponent on the projectile to stop further collision and movement.

One monobehaviour script to run this called ProjectileLauncher, it contains code to instantiate entities from a prefab or from scratch in C#.