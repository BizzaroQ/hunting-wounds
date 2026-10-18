# Hunting Wounds (Hunter's Delight)

A Vintage Story mod that applies movement speed modifiers, blood particles, and temporary blood trail blocks to damaged entities in singleplayer and multiplayer.

---

## Features

- **Movement Speed Modifiers**
  - On taking damage, an entity has an `80%` chance to receive a walk speed penalty (`-15%` per hit, capped at `-90%`) and a `20%` chance to receive a walk speed increase (`+20%` per hit).
- **Task-Based Bleeding**
  - Damaged entities spawn blood particles and place blood trail blocks every `0.5` seconds while executing `Flee`, `SeekEntity`, or `MeleeAttack` AI tasks.
  - Bleeding stops after the entity has been out of those AI tasks for `2.5` seconds, and resumes if the entity takes damage again.
- **Blood Trail Aging & Ground Restoration**
  - Placed blood trails start at variant `bloodtrail-day0` (red), transition to `bloodtrail-day3` (brown) at 50% of their lifespan, and are removed at 100% of their lifespan (`1.0` in-game day by default).
  - Blood trails can replace air, `tallgrass-*`, and `snowlayer-1` blocks. When a blood trail expires, any replaced `tallgrass-*` or `snowlayer-1` block is restored.
- **Wound Recovery**
  - Movement speed modifiers and hit counts reset after `24.0` in-game hours without taking further damage.
- **Multiplayer Support**
  - Server-side state tracking, block and attribute synchronization, and optional land claim checks.

---

## Patched Entities

The `huntingwounds` behavior is patched onto the following entity groups (requires the `harvestable` behavior):
- `deer-*`, `moose-*`, `gazelle-*`, `goat-*`, `pig-*`, `sheep-*`
- `hare-*`, `raccoon-*`, `fox-*`, `chicken-*`

---

## Installation

1. Download `Hunters Delight <version>.zip`.
2. Place the `.zip` file into the Vintage Story `Mods` directory:
   - **Windows:** `%AppData%\VintagestoryData\Mods`
   - **Linux:** `~/.config/VintagestoryData/Mods`
   - **macOS:** `~/Library/Application Support/VintagestoryData/Mods`
3. **Multiplayer:** Place the `.zip` file into the server's `Mods` folder. Clients will download the mod automatically upon connecting.

---

## Configuration

On server startup, the mod reads or generates `VintagestoryData/ModConfig/huntingwounds.json` (using defaults from `assets/huntingwounds/config/huntingwounds.json`):

```json
{
  "BloodTrailLifespanDays": 1.0,
  "WoundRecoveryHours": 24.0,
  "WalkSpeedPenaltyPerHit": -0.15,
  "AdrenalineSpeedBoostPerHit": 0.20,
  "SlowdownChancePerHit": 0.80,
  "StopBleedingAfterCalmSeconds": 2.5,
  "RespectLandClaims": false
}
```

| Setting | Default | Description |
| :--- | :---: | :--- |
| `BloodTrailLifespanDays` | `1.0` | In-game days before a blood trail block is removed. Switches from `day0` (fresh) to `day3` (dried) at 50% of this value. |
| `WoundRecoveryHours` | `24.0` | In-game hours after the last hit before hit count and walk speed modifiers reset. Set to `<= 0` to disable. |
| `WalkSpeedPenaltyPerHit` | `-0.15` | Walk speed modifier added per hit when a slowdown occurs (`-0.15` = `-15%`). Minimum total modifier is `-0.90`. |
| `AdrenalineSpeedBoostPerHit` | `0.20` | Walk speed modifier added per hit when an adrenaline speed boost occurs (`0.20` = `+20%`). |
| `SlowdownChancePerHit` | `0.80` | Probability (`0.0`–`1.0`) that a hit applies `WalkSpeedPenaltyPerHit` instead of `AdrenalineSpeedBoostPerHit`. |
| `StopBleedingAfterCalmSeconds` | `2.5` | Real-time seconds an entity must be outside `Flee`, `SeekEntity`, or `MeleeAttack` tasks before bleeding stops. |
| `RespectLandClaims` | `false` | If `true`, prevents blood trail blocks from being placed inside claimed land. |

---

## Project Structure

```text
├── assets/
│   └── huntingwounds/
│       ├── blocktypes/
│       │   └── bloodtrail.json            # Block definition and stage variants
│       ├── config/
│       │   └── huntingwounds.json         # Default configuration
│       ├── lang/
│       │   └── en.json                    # English localization for blood trail blocks
│       ├── patches/
│       │   └── huntingwounds.json         # Entity behavior patches
│       ├── shapes/block/
│       │   └── bloodtrail.json            # Block shape
│       └── textures/block/
│           └── blood-day0..4.png          # Block textures
├── src/
│   ├── HuntingWounds.cs                   # ModSystem entry point and HuntingWoundsConfig
│   ├── BlockBloodTrail.cs                 # Blood trail block behavior
│   ├── BlockEntityBloodTrail.cs           # Blood trail aging and ground cover restoration
│   └── EntityBehaviorHuntingWounds.cs     # Entity wound tracking, speed modifiers, and bleeding
└── modinfo.json                           # Mod metadata
```

