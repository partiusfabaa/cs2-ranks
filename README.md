

# Player Ranking
Each player is assigned a rank based on their accumulated experience points. Ranks range from "None" to the prestigious "The Global Elite."

# Experience Points
Players earn experience points for various in-game achievements, such as kills, assists, and MVP awards. Experience contributes to both their rank and overall score.

<img src="https://github.com/partiusfabaa/cs2-ranks/assets/96542489/e8f76e69-6d18-48e8-8d8d-c45a34142f99" width="421" height="160">

# Level Progression
Players progress through levels based on their experience points. Level achievements are announced in the chat, providing recognition for their dedication and skill.

# Dynamic Clan Tags
Players' clan tags dynamically change based on their current level, allowing others to see their rank at a glance.

<img src="https://github.com/partiusfabaa/cs2-ranks/assets/96542489/898dbf34-f262-4950-b003-1d5d45a68f1f" width="206" height="45">

# Events and Rewards
The plugin tracks events like round victories, defeats, and MVP awards, awarding or deducting experience points accordingly.

# Database Integration
Player statistics are stored in a MySQL database, allowing for persistent tracking of player progress even after disconnecting.

# Top Players List
Players can check the top-ranking players on the server, including their rank, experience points, and kill/death ratio.
<img src="https://github.com/partiusfabaa/cs2-ranks/assets/96542489/0ba22ec4-bbeb-4e8b-b3ff-8a6363766b4e" width="308" height="40">

# Command for Configuration Reload
Server administrators can reload the plugin configuration on-the-fly using the css_lr_reload console command.

# Customizable Configuration
Server administrators can fine-tune the plugin's behavior, including experience point rewards for specific events, minimum player count for XP gain, and more.

# Installation
1. Install [CounterStrike Sharp](https://github.com/roflmuffin/CounterStrikeSharp) and [Metamod:Source](https://www.sourcemm.net/downloads.php/?branch=master)
3. Download [Ranks](https://github.com/partiusfabaa/cs2-ranks/releases/tag/v1.0.1)
4. Unzip the archive and upload it to the game server

# Commands

| Command          | Description                      |
|------------------|-------------------------------|
| `css_lr_reload` | reloads the configuration (server console only)          |
| `css_lvl` or `!lvl` | opens a menu where you can view all ranks |
| `css_top` or `!top` or `top` | displaying the top 10 players in chat |
| `css_rank` or `!rank` or `rank` | chat statistics display  |


# Config

## Events

- **EventRoundMvp**: 12     &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;// Amount of experience for MVP
- **EventPlayerDeath**:
  - Kills: 13              &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;// Amount of experience gained per kill
  - Deaths: 20             &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;// The amount of experience you lose per death
  - Assists: 5             &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;// The amount of experience you get for assisting in a kill
  - KillingAnAlly: 6       &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;// The amount of experience you lose for killing an ally
- **EventPlayerBomb**:
  - DroppedBomb: 5         &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;// The amount of experience lost for losing a bomb
  - PlantedBomb: 3         &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;// The amount of experience you get for planting a bomb
  - DefusedBomb: 3         &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;// The amount of experience you get for defusing a bomb
  - PickUpBomb: 3          &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;// The amount of experience you get for picking up a bomb
- **EventRoundEnd**:
  - Winner: 5             &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;// The amount of experience gained per round won
  - Loser: 8              &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;// The amount of experience lost for losing a round


## Weapons
 You get extra experience for killing with this weapon (you can add your own weapons without `weapon_`)
- knife: 5
- awp: 2

## Database connection

- Host: "HOST"
- Database: "DATABASE"
- User: "USER"
- Password: "PASSWORD"
