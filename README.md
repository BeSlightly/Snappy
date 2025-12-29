<div align="center">
  <img src="snappy.png" alt="Snappy Logo" width="150" />
  <h1>Snappy</h1>
  <p><strong>(Formerly XIVSnapper)</strong></p>
  <p>A forked and maintained mod originally based on work by <a href="https://github.com/eqbot">@eqbot</a>, <a href="https://github.com/ViviAshe">@ViviAshe</a> and <a href="https://github.com/astrodoobs">@astrodoobs</a>.</p>

  <!-- Badges -->
  <a href="https://github.com/BeSlightly/Snappy/releases"><img src="https://img.shields.io/github/v/release/BeSlightly/Snappy?style=for-the-badge&color=blue" alt="Latest Release"></a>   <a href="https://github.com/BeSlightly/Snappy/releases"><img src="https://img.shields.io/github/v/release/BeSlightly/Snappy?include_prereleases&label=Testing&style=for-the-badge&color=orange" alt="Latest Testing (Pre-release)"></a>
  </div>

---

## 📖 What is it?

Ever wanted to share your character's appearance with a friend, only to realize you'd have to dig through your mod list or settle for the limited Mare Character Data Format (MCDF)? **Snappy solves that problem.**

Snappy is a plugin designed to save and load your character’s full appearance - mods and all - with a single click. No restrictions, no compromises. It creates a single-character mod collection that captures everything exactly as it is.

> **Share your setup with friends, and they can load your character just as you see it, then customize it however they like.**

No more relying on MCDF. Just a clean, accurate snapshot of your character, ready to use.

---

## 🛠️ Supported Mare Forks

 Supported Mare forks:

*   [Lightless Sync](https://git.lightless-sync.org/Lightless-Sync/LightlessClient)
*   [Snowcloak](https://github.com/Eauldane/SnowcloakClient)
*   [Player Sync](https://github.com/universalconquistador/MareSynchronosClient)

*Note: For unsupported Mare forks, enable "Use Penumbra/Customize+/Glamourer (fallback)" in Settings to snapshot via live Penumbra/Glamourer/C+ data.*

---

## 📥 Installation

Add this custom repo to Dalamud:

```
https://github.com/BeSlightly/Snappy/raw/refs/heads/master/repo.json
```

### Legacy Version (0.2)
The old version (0.2) is still available on the `0.2.0-dev` branch.
*   ⚠️ Migrated Snapshots will not be compatible with that version.
*   ⚠️ This version will eventually stop functioning.

```
https://github.com/BeSlightly/Snappy/raw/refs/heads/0.2.0-dev/repo.json
```

---

## 🚀 How to Use

1.  **Set a working directory** in the main window (bottom left).
2.  **To save snapshots**:
    *   Select an actor in the actor list.
    *   Press the save icon 💾.
    *   *If you already have a Snapshot, you can append to the existing save. Hitting the `save` icon while having a Snapshot appends for you.*
3.  **To load snapshots**:
    *   Enter **GPose**.
    *   Select an actor.
    *   Use the **Load** button to pick a snapshot folder.
    *   *Tip: Best performance comes from using [Brio](https://github.com/AsgardXIV/Brio)-spawned actors. I test against those, and if you load onto anything else, you're on your own.*

---

## ⚠️ Disclaimer

The following is a message from the previous developer, which remains relevant to this fork:

> **I am acutely aware of the controversial nature of this plugin.**
>
> I've decided to maintain this plugin as it's served incredible purpose to me for GPosing myself and my friends. This tool is being maintained explicitly for **character customization and mod sharing**. Nothing more, nothing less. It’s here to make it easier to preserve, export, and share the way a character looks, especially among friends or within a private modding circle.
>
> This plugin retains its original ability to capture appearances from other players: what this means is that if they’re using mods, you’ll obtain them too. That makes consent **absolutely a non-negotiable**.
>
> **Always ask. Always get permission.**
>
> Please use this tool responsibly. Do not use it to pirate paid mods, and do not use it to exploit characters that aren’t your own. Support the creators who pour countless hours into their work.
>
> If the existence of this mod upsets you, understand this: it is being maintained with honest, transparent, and constructive intentions. Its purpose will always serve primarily as utility and never exploitation. It’s about preserving creativity, not crossing lines.

