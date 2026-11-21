# RimWorld pacing review: 20 expedition objectives

Updated 2026-11-21. Research and design only; no game launch or gameplay-code edits.

## Decisions

Target **25–45 in-game days** for a squad of 3–5. Remove the mood requirement from Seasonal Endurance. Keep the Armory as an advanced conditional objective, allowing completion beyond day 30 when research is missing. Weather rates are adjustable tuning parameters; the estimates below assume they are retuned for the selected duration.

The earlier report's quotas do not mathematically enforce its advertised durations. More colonists, larger fields, ruins, inherited research, or good skills can finish them faster. The candidates below therefore propose **visible minimum residence times** alongside useful base-building requirements. These timers are design proposals, not current functionality. Without them, many production/building objectives should be advertised as variable-duration tasks.

## Sources and corrected baseline

Primary evidence: installed **1.7.5014 rev1195** game XML, read without launching the game, and this repository's blueprint/objective definitions. Game definition root: `C:/Program Files (x86)/Steam/steamapps/common/RimWorld/Data/Core/Defs/`. Version-specific observations do not certify identical 1.5/1.6 values. Public wiki pages provide readable cross-checks. Earlier unverified Reddit links and precise storyteller deadlines are removed.

### Time and development

The third speed setting targets **6× normal speed**, not 3×: 360 ticks/second and about **2.78 real minutes per 60,000-tick day**. At that rate 25–45 days is **69–125 minutes unpaused**. Idle acceleration may shorten this; planning, combat, and performance limits lengthen it. An hour is not guaranteed. [Time](https://rimworldwiki.com/wiki/Time).

The following is a practical design envelope for a temperate start, not a measured average:

| Days | Plausible development |
| --- | --- |
| 1–2 | Roof supplies, barracks/beds, campfire, butcher spot, table/seats; sow rice. |
| 3–6 | Hunt/gather to bridge food, prepare cover/choke, begin stonecutting or research if available. |
| 6–10 | First normal-soil rice harvest; kitchen/freezer can operate if drafted and supplied. |
| 10–15 | Potatoes, healroot and cotton contribute; separate dirty workshops from cooking, expand rooms and defenses. |
| 15–21 | Basic functioning base is already possible; first normal-soil corn approaches maturity. |
| 22–30 | Additional harvests, masonry, research and machining are plausible choices. |
| 31–45 | Slow textiles and industrial projects provide longer loops; missing research or bad weather can push them later. |

A functioning base needs sheltered sleeping space, protected food/medicine storage, sustainable food, cooking/butchering, dining/recreation and defensible approaches. It does not universally need a freezer or private bedrooms. A small workable base can exist in roughly 3–10 days under favorable conditions; this is an estimate. No fixed first/second/fourth-raid schedule is assumed.

### Crops and food

Nominal growth excludes nightly rest. The common ideal calendar estimate multiplies growDays by 24/13, then accounts for fertility/light/temperature. [Plants](https://rimworldwiki.com/wiki/Plants).

| Crop | Installed nominal growDays | Approx. ideal calendar days, normal soil | Base mature yield | Restriction |
| --- | ---: | ---: | ---: | --- |
| Rice | 3 | 5.54 | 6 | Repeated sowing costs labor |
| Potato | 5.8 | 10.71 | 11 | Earlier 11.08 estimate used a different base |
| Corn | 11.3 | 20.86 | 22 | No hydroponics |
| Healroot | 7 | 12.92 | 1 medicine | Plants 8 |
| Cotton | 8 | 14.77 | 10 cloth | Soil/light/temperature |
| Devilstrand | 22.5 | 41.54; ~29.67 on 140% soil | 6 fabric | Plants 10, research, no hydroponics |

Local source: `ThingDefs_Plants/Plants_Cultivated_Farm.xml` and inherited plant bases. Harvest skill/difficulty and losses change actual yield. Count whole completed harvests, not fractional crop cycles.

For planning, allow roughly two meals per ordinary adult per day: 6/day for three, 10/day for five. Fifty starting meals bridge roughly 8 or 5 days. Hunger modifiers can change this. Three pawns eating simple meals for 30 days use about 1,800 raw-food items; subtract starting meals actually eaten when estimating local production.

Simple meals use 0.5 raw nutrition, or ten ordinary 0.05-nutrition ingredients. Packaged meals use 0.3 plant plus 0.3 animal nutrition: normally six of each, require Cooking 8 and research, and cannot be made at campfires. Simple meals rot in about four unrefrigerated days; 80 at once needs preservation or a late batch. [Simple meal](https://rimworldwiki.com/wiki/Simple_meal), [Packaged survival meal](https://rimworldwiki.com/wiki/Packaged_survival_meal). Local recipes: `RecipeDefs/Recipes_Food.xml`.

### Work and material anchors

Daily output is not fixed. As an illustrative budget use 25,000 active work ticks per pawn-day (10 in-game hours), before travel, hauling, fighting, refueling and downtime. Three pawns then supply 75,000 active ticks/day. **25 colonist-days of work is about 8.3 colony days at full allocation**, not 25 calendar days.

| Task | Installed definition / consequence |
| --- | --- |
| Mature oak | 46 wood, 1,400 harvest work. At 100% relevant work speed, ~820 wood per illustrative workday before logistics; immature trees/weak growers reduce it. |
| Compacted steel | 1,500 HP; installed base yield 40 steel/cell. 150 steel is four full-yield cells. Time depends on Mining Speed and runtime behavior; no universal daily output is claimed. |
| Natural stone mining | Produces chunks probabilistically, not blocks. Loose chunks can bypass mining. |
| Stonecutting | 20 blocks per chunk, 1,600 work: ~312 blocks per illustrative workday before logistics/room/work modifiers. |
| Wall | 5 stuff, 135 base work; material offsets/factors apply. |
| Door | 25 stuff, 850 base work. Wooden doors avoid slow stone-door passage. |
| Bed | 45 stuff, 800 base work. Three beds cost 135 wood. |
| Chemfuel from wood | 70 wood → 35 fuel for 2,000 work. Twelve batches: 840 wood, 24,000 work, 420 fuel. |
| Heavy SMG | 75 steel, 4 components, 24,000 work; Crafting 5. |
| Flak vest | 60 steel, **30 cloth**, 1 component, 9,000 work; Crafting 4. |
| Duster | 80 fabric; **10,000** base work before material factors, not the previous 28,000. |

Local evidence: tree defs (`Plant_TreeOak`); `ThingDefs_Buildings/Buildings_Natural.xml`, `Buildings_Structure.xml`, `Buildings_Furniture.xml`; recipe defs `MakeStoneBlocksBase`, `Make_ChemfuelFromWood`; `ThingDefs_Misc/Weapons/RangedIndustrial.xml` and apparel definitions. Base work is not elapsed job duration. Cross-checks: [Stonecutter's table](https://rimworldwiki.com/wiki/Stonecutter%27s_table), [Wall](https://rimworldwiki.com/wiki/Wall), [Heavy SMG](https://rimworldwiki.com/wiki/Heavy_SMG), [Flak vest](https://rimworldwiki.com/wiki/Flak_vest), [Duster](https://rimworldwiki.com/wiki/Duster).

### Research and the Armory concern

| Project | Installed points | Immediate prerequisites |
| --- | ---: | --- |
| Stonecutting / Complex furniture | 300 each | None; may start known |
| Smithing | 700 | None |
| Machining | 1,000 | Electricity + Smithing |
| Gunsmithing | 500 | Machining |
| Blowback operation | 500 | Gunsmithing |
| Gas operation | 1,000 | Blowback operation |
| Flak armor | 1,200 | Machining |
| Microelectronics | **3,000** | Electricity |
| Precision rifling | 1,400 | Gas operation + Microelectronics; hi-tech bench |
| Devilstrand | 800 | Plants 10 separately needed to sow |
| Packaged survival meals | 500 | Nutrient paste |
| Biofuel refining | 700 | Electricity |

Local source: `ResearchProjectDefs/`. Simple research bench costs 75 stuff + 25 steel, 2,800 base build work, with research factor 0.75. Using the wiki formula, a pawn at displayed Research Speed 100%, neutral other factors and 25,000 active ticks produces **154.7 points/day**: 0.00825 × 0.75 × 25,000. At 150%, ~232/day. Room/difficulty modifiers still apply. [Research](https://rimworldwiki.com/wiki/Research).

Starting with Electricity but none of the rest, SMGs plus flak need **4,900 points**: 700 + 1,000 + 500 + 500 + 1,000 + 1,200. This is about **21–32 dedicated research days under those assumptions**, plus setup. Material gathering can overlap. Three SMGs and three vests add **405 steel, 15 components, 90 cloth and 99,000 base crafting work**, excluding infrastructure.

Assault rifles require the separate Precision rifling path, including Microelectronics and hi-tech infrastructure. They cannot share the SMG timing estimate. The user's concern about an Armory exceeding 30 days is justified. Keep it conditional on existing research, researcher capacity, resources and crafting skill.

Devilstrand also needs correction: three dusters need **240 fabric**, not 200, and at least 40 successful mature plants. Research/sowing delay plus ~41.5 normal-soil days can exceed the target. [Devilstrand plant](https://rimworldwiki.com/wiki/Devilstrand_mushroom).

## Blueprint audit

Checked `Roguelike/Defs/Roguelike_Rewards.xml` and `Roguelike/Source/Rewards.cs`.

| Blueprint | Relevant contents |
| --- | --- |
| Crafting | Stonecutter, tailoring benches, smithies, smelter |
| Research | Simple and hi-tech benches |
| Kitchen | Stoves and nutrient paste dispenser |
| Power | Wood generator, solar/wind, batteries, conduits/switches |
| Climate | Heater, cooler, vents, passive cooler |
| Defenses | Sandbags, barricades, spike traps, mini-turrets |
| Farming | Hydroponics and sun lamp |
| Medical | Hospital beds and vitals monitor |
| Comfort | Armchairs, end tables, dressers, plant pots |
| Storage | Shelves |

**Machining tables and biofuel refineries are absent from all current blueprint groups.** They are therefore not blueprint-locked under the current gate, but still need vanilla research/materials. Adding them to Crafting would be a separate design change. Ordinary beds marked medical do not require Medical. Basic tables/chairs outside these lists do not require Comfort.

Blueprints and research are separate prerequisites. Meta-unlocking Power into the reward pool is not the same as drafting it this run. Research can persist across nodes in the continuing game; check the actual faction state.

## Twenty proposed objectives

**Shared interpretation:** N is squad size on entry, frozen at 3–5 for quota scaling. Earliest departure is an explicit proposed timer; the window is a tuning estimate, not a playtested guarantee. “Shelter” means a roofed enclosed reachable room with N usable beds. “Maintain” means consecutive days, with a short interruption tolerance to be defined.

Count local production/construction rather than starting supplies, imports, purchases, ruins or repeated rebuild credit. Require sufficient map resources, skill, research access, growing season and blueprints before offering a candidate. Most use only Core mechanics. Recipe and food restrictions from DLC/ideology still need checking.

| # | Objective and concrete goal | Earliest day / intended window | Requirements and feasibility |
| --- | --- | --- | --- |
| 1 | **Great Granary:** harvest 1,500 corn locally; finish with 20N meals and shelter. | 25 / 25–35 | ≥69 successful plants before losses; ~21 days normal soil or ~15 rich. Bridge with rice/hunting. No mandatory outdoor-farming blueprint; large meal reserve favors Climate + Power. |
| 2 | **Harvest Rotation:** locally harvest 600 rice, 600 potatoes, 600 corn; retain 200 edible raw food and shelter. | 25 / 25–35 | About 100/55/28 successful plant-harvests respectively. Outdoor cultivation needs viable season/weather; no mandatory blueprint. Larger fields can finish sooner than the timer. |
| 3 | **Rice Cooperative:** harvest 1,800 rice, cook 120 simple meals cumulatively, build N private bedrooms. | 25 / 25–32 | 300 successful rice harvests; six cycles from 50 tiles ~33 days normal soil, larger plot faster. Campfire and basic building suffice. |
| 4 | **Medicinal Garden:** harvest 60 herbal medicine, retain 30, and build a separate roofed 2-bed medical room. | 25 / 25–32 | Plants 8; ~13-day normal-soil cycle. Ordinary medical beds suffice, no Medical blueprint. Verify growing season and medicine storage. |
| 5 | **Textile Settlement:** harvest at least max(300,80N) cloth, craft N cloth dusters/parkas, build N bedrooms. | 25 / 25–35 | Crafting; Complex clothing known or Research access. Cotton ~15 days normal soil. Three dusters use 240 cloth, five use 400. |
| 6 | **Devilstrand Weaver:** harvest 80N devilstrand and craft N devilstrand dusters. | 30 / 32–45+, conditional | Crafting, Devilstrand research, Plants 10, Complex clothing. ≥40 successful plants for three coats. Normal soil plus research delay easily exceeds 45 days; offer with early research and viable season/greenhouse. |
| 7 | **Stone Fortress:** build 200 stone walls, 8 doors of any material, 80 stone floor cells, and shelter. | 25 / 25–35 | Crafting; Stonecutting known or Research. Walls+floors: 1,320 blocks/66 chunks. All-stone doors add 200 blocks/10 chunks; wooden doors remain valid. Counts should form useful rooms/enclosure. |
| 8 | **Mason's Settlement:** produce 2,000 blocks, use ≥1,000 in finished shelter/workshop complex, retain 400. | 25 / 25–35 | Crafting and Stonecutting. 100 chunks, 160,000 cutting work before logistics. Require enough loose rock/mineable stone; production can finish well before day 25. |
| 9 | **Private Quarters:** N separate roofed bedrooms ≥12 interior cells each with a bed; shared dining room ≥25 cells with table and N seats. | 25 / 25–30 | Basic structures suffice. No mood/impressiveness/wealth threshold. Rooms naturally finish early; residence timer supplies duration. |
| 10 | **Complete Settlement:** Private Quarters plus separate ≥12-cell kitchen/workshop, 2-bed medical room, ≥25-cell powered freezer containing 20N frozen meals. | 30 / 30–40 | Kitchen + Climate + Power. Ordinary medical beds avoid Medical dependency. Check actual freezer temperature below 0°C, not cooler setpoint. |
| 11 | **Cold-Chain Depot:** maintain ≥600 edible raw food below 0°C in ≥25-cell freezer for 7 days; finish with 20N meals. | 25 / 25–35 | Climate + Power; require running cooler for active refrigeration rather than natural winter shortcut. Campfire cooking allowed. Escalating heat affects cooler capacity. |
| 12 | **Winter Refuge:** maintain N beds in roofed shelter at 10–30°C for 7 days; retain 10N meals. | 30 / 30–40 | Cold variant. Available heating/fuel route, often Climate + Power; campfire heating valid if adequate. No mood condition. Weather adjusted to survivable endpoint. |
| 13 | **Heat Refuge:** maintain N beds in roofed shelter at 10–30°C for 7 days; retain 10N meals. | 30 / 30–40 | Climate + Power, sensible cooler exhaust and heat trajectory. Passive cooling cannot counter unlimited heat. |
| 14 | **Indoor Harvest:** harvest 600 rice in roofed artificially lit growing space with viable temperature; retain 10N meals. | 25 / 25–35 | Farming + Power, usually Climate. Greenhouse soil avoids Hydroponics research; basins require it. Verify peak sun-lamp supply and count qualifying harvests. |
| 15 | **Research Outpost:** earn 4,000 new research points locally, have Machining and Microelectronics, shelter and separate research room. | 30 / 30–45+ | Research. Missing named techs total 4,000 before Smithing/Electricity; already-known tech grants no new-point credit. Need enough eligible uncompleted research. Tribal/weak research can exceed 45 days. |
| 16 | **Field Armory:** craft N Heavy SMGs and N flak vests locally; shelter and 10N meals. | 30 / 30–45+ | Power, machining table (currently blueprint-ungated), Crafting skill 5. Research access if chain missing. Per pawn 135 steel, 5 components, 30 cloth, 33,000 crafting work. Crafting blueprint is useful but not required for machining recipes themselves. |
| 17 | **Precision Arsenal:** craft 3 assault rifles and 3 flak vests locally; shelter and 30 meals. | 35 / 35–45+, advanced | Power, machining, Crafting skill 6, Precision rifling path/hi-tech research as needed. Products: 360 steel, 24 components, 90 cloth, 147,000 work. Offer only with most tech known. |
| 18 | **Automated Strongpoint:** 4 functioning powered mini-turrets, 20 barricade/sandbag cells, shelter, defeat 3 scripted node assaults. | 30 / 30–40 | Defenses + Power; Gun turrets research and prerequisites. Turrets cost 400 steel/12 components before grid. Example waves days 10/20/30; count resolved waves rather than storyteller assumptions. Defense hybrid, not pure non-combat. |
| 19 | **Expedition Reserves:** locally cook 100 packaged meals and refine ≥400 fuel; retain both and shelter. | 30 / 30–42+ | Kitchen + Power; refinery currently blueprint-ungated. Cooking 8, packaged-meal/Nutrient paste and Biofuel refining research. Meals need normally 600 plant+600 animal items. Twelve fuel batches consume 840 wood. Exclude starting 50 meals from production credit. |
| 20 | **Seasonal Endurance:** survive 30 days with zero entry-squad deaths; shelter and 10N meals at departure. | 30 / 30 if ready | **No mood requirement.** No universal blueprint; check viable heating/cooling/food route. Record deaths when they happen; casualties do not lower N. Clarify later joiners/resurrection/departure rules. |

Start playtesting the Granary, Rotation, Medicinal Garden, Stone Fortress, Private Quarters, Cold-Chain Depot, thermal refuges and Seasonal Endurance. They offer understandable requirements. Keep textiles, research, indoor farming and manufacturing conditional on actual capabilities. For five workers inherited research may make production much quicker; for three workers missing the whole chain can put it beyond 45 days.

If explicit minimum stays feel too passive, test shorter node lengths rather than asserting the quotas inherently last a month. Crop stages and sustained facility operation can provide meaningful intermediate progress, but they remain variable without a timer.

## Weather and implementation

Per the user's follow-up, **weather can be adjusted**. Retune it with each duration rather than rejecting long objectives. At a constant baseline ±4°C/day after a 2-day grace, day 30 adds ±112°C before multipliers. As a starting calculation, an extra 35°C over the remaining 28 days implies ~1.25°C/day before multipliers. This is not a tested safe value; act/ascension/pace factors and biome temperature still apply. Toxic/psychic conditions need separate budgets.

Set targetDays to the new intended duration. Account for crop/research setup in progress: otherwise a long crop has little counted output early, appears behind schedule, and increases weather before its first harvest.

Current `Node.cs` selects one of Stockpile, Build, Survive, Escort, KillAll; it does not yet support these AND conditions, local production attribution, material/room checks, sustained timers or universal minimum stays. These are proposals, not ready-to-load presets. Research and blueprint eligibility checks belong before node selection; a future reward draft cannot be relied on to unlock a required building.

## Twenty additional objectives unlocked by rewards (21–40)

These extend the original 20 rather than replace them. The aim is for a reward to open a recognizable new type of expedition, making blueprint picks matter beyond faster production.

**Eligibility contract:** every listed blueprint must already be drafted into the current run, and every listed research project must already be completed **before the objective is offered**. These are AND requirements unless explicitly stated otherwise. Meta-unlocking a card into the reward pool is insufficient. Required research should not be left as homework that unexpectedly adds another month to a manufacturing objective.

Current reward code grants blueprints but has no research reward kind. Thus these candidates can currently be tied to reward-owned blueprints plus completed research. If direct research rewards are added, use the same completed-project check; track reward provenance separately only if objectives must require a project specifically obtained from a reward rather than researched normally. The Research blueprint unlocks benches, not completed projects.

All candidates retain the earlier shelter, node-local production, resource feasibility and N-on-entry rules. Proposed minimum departure days remain visible timers; these are **not** claims that the quota inherently takes that long. Most target day 25–35; advanced facilities use day 30–45. No mood victory conditions are added. All thresholds below are initial design proposals.

| # | Objective / concrete completion rule | Must already be unlocked | Why it creates a base; eligibility checks |
| --- | --- | --- | --- |
| 21 | **Paste Commissary:** build a working nutrient paste dispenser with connected hoppers; supply 120 locally dispensed meals actually eaten by squad members, with 300 raw food still in hoppers at departure. Minimum day 25. | **Kitchen + Power**; **Nutrient paste** research. | Requires a powered food room, sustained ingredient production and ongoing habitation. Count dispenser-generated meals when eaten, not repeatedly dropped stacks. Offer only if squad food rules permit paste. |
| 22 | **Frontier Restaurant:** cook 120 fine meals locally, have 20 unspoiled at departure, and maintain a separate kitchen, dining room with N seats, and powered freezer for 5 days. Minimum day 25. | **Kitchen + Climate + Power**; the chosen fine-meal recipe usable. | Sustains hunting/farming and refrigeration. Require the relevant cook skill and ingredient categories; accept suitable vegetarian/carnivore variants with their actual costs. |
| 23 | **Banquet Hall:** cook 60 lavish meals locally and serve 30 of them to the squad in a dining room with N armchairs; retain 10. Minimum day 25. | **Kitchen + Comfort**; a usable lavish-meal recipe. | Adds expensive food production and a furnished shared space. Eligibility checks cook skill, dietary rules and ingredients. A freezer is optional if cooking small batches; no required happiness or impressiveness score. |
| 24 | **Hydroponic Apothecary:** build 6 powered basins and harvest 80 herbal medicine from those basins; retain 40 medicine in protected storage. Minimum day 25. | **Farming + Power + Climate**; **Hydroponics**. | Creates a temperature-controlled indoor medical crop industry, distinct from the outdoor garden. Plants 8 and enough power for basins and sun lamp are mandatory. Count basin-grown harvests only. |
| 25 | **Cotton Under Glass:** build 8 powered basins, harvest 500 cloth from them, and keep the grow room within the viable temperature range for 5 days. Minimum day 25. | **Farming + Power + Climate**; **Hydroponics**. | Commits substantial steel, components and electricity to a non-food crop while food still needs a solution. Cotton is supported in the installed hydroponic sow tags. Do not count imported cloth. |
| 26 | **Uniform Workshop:** build two electric tailoring benches; locally craft N dusters or parkas, N shirts and N pants, with all 3N garments present at departure. Minimum day 25. | **Crafting + Power**; **Complex clothing**. | Requires sustained textile supply, a roofed workshop and powered production. Count garments made at these benches; skill/material availability must be feasible. Garment quality and mood are unrestricted. |
| 27 | **Salvage Foundry:** build a powered electric smelter and recover 600 steel locally through eligible smelting bills; retain 300 and keep a finished workshop. Minimum day 25. | **Crafting + Power**; **Electricity** (the installed smelter has no separate Electric smelting project). | Turns wreckage and unwanted metal equipment into an industrial loop. Offer only with enough salvage spawned or guaranteed; exclude items crafted locally solely to smelt them for circular credit. |
| 28 | **Component Works:** build a powered fabrication bench, make 20 industrial components locally, and retain 10 for departure. Minimum day 30. | **Power + Research**; **Fabrication**. | Turns mined steel into a workshop supply chain. Fabrication bench is currently outside blueprint groups, so completed technology and the reward-owned Power/Research gates are explicit. Check recipe skill, bench materials and steel availability before offering. Twenty components consume 240 steel and 100,000 base crafting work, excluding the bench. |
| 29 | **Field Hospital Commission:** build 2 actual hospital beds in a dedicated room with at least 20 sterile floor cells; keep the room, beds and 30 medicine ready for 7 days. Minimum day 30. | **Medical + Power**; **Hospital bed + Sterile materials**. | Requires advanced beds and a silver/steel supply, rather than ordinary beds flagged medical. Bed power, cleanliness, access and protected medicine storage matter; no need to injure pawns or wait for patients. |
| 30 | **Monitored Ward:** build 3 powered hospital beds, each linked to a working vitals monitor, and maintain the ward at 10–30°C with 40 medicine for 7 days. Minimum day 35. | **Medical + Power + Climate**; **Hospital bed + Vitals monitor**. | A deliberately later medical variant; one monitor may serve multiple beds if the game links it correctly. Count actual active links. Vitals research has the deeper Multi-analyzer prerequisite; check it is completed before showing the objective. |
| 31 | **Battery Reserve:** build 4 batteries and two distinct generating units; accumulate 10 days with at least 1,200 Wd stored while the grid powers an active freezer and kitchen. Minimum day 25. | **Power + Climate + Kitchen**; **Battery** and any required generator research. | Requires useful loads alongside reserve generation. Wd means the game's stored-energy unit, not watts. Count qualifying time cumulatively so one short circuit does not erase an entire expedition; imported charged batteries earn no construction credit. |
| 32 | **Renewable Campus:** build 2 solar generators, 2 wind turbines and 4 batteries; power a freezer and N-bedroom settlement with no fueled generation connected for 7 days. Minimum day 30. | **Power + Climate**; **Solar panel + Battery**, plus prerequisites of the chosen turbines. | Weather and night cycles make storage and grid layout relevant. Require active loads and a sensible uptime threshold; disconnecting everything cannot qualify. Check buildable ground and component supply. |
| 33 | **Trapworks Perimeter:** build an enclosed settlement, stock 200 reserve wood/steel, and keep 12 armed spike traps plus 24 cover cells ready for 7 cumulative days. Minimum day 25. | **Defenses**; all required trap/cover research already known. | A defensive construction objective without compulsory combat. Count armed traps, enforce a reachable safe squad entrance, and allow replenishing destroyed traps. Do not require lucky storyteller raids to finish. |
| 34 | **Furnished Retreat:** N private bedrooms each with bed, linked end table and dresser; shared recreation room with N armchairs and 4 plant pots. Maintain the usable layout for 7 days. Minimum day 25. | **Comfort**; **Complex furniture**. | Makes the Comfort reward open a dedicated settlement objective. Shared dressers are allowed if correctly linked. No mood, beauty, furniture quality or room-impressiveness threshold. |
| 35 | **Quartermaster Depot:** build 12 shelves in a roofed warehouse and fill them with at least 600 wood, 300 steel, 600 edible raw food and 30 medicine at departure. Minimum day 25. | **Storage**; **Complex furniture** and other shelf prerequisites if required by the installed defs. | Makes storage organization itself matter. Check quantities physically held on shelves rather than a global resource count. All target supplies must be gathered/produced locally; verify map steel and cultivation viability. |
| 36 | **Swordsmith's Forge:** build a fueled or electric smithy in a workshop and craft N steel longswords plus 3 plate armors locally; retain the products. Minimum day 25. | **Crafting**; **Smithing + Long blades + Plate armor**. | A low-electricity manufacturing branch for squads lacking Power. Electric smithy additionally requires Power. Plate armor requires Crafting 7. Verify steel and other recipe requirements; hauling, armor production and survival compete for the same small workforce. |
| 37 | **Munitions Depot:** craft 40 high-explosive mortar shells locally, retain 30 in a separate roofed magazine, and keep a machining workshop powered for 5 days. Minimum day 30. | **Power + Research**; **Mortars + Machining**, and any additional shell-recipe prerequisites. | Requires a real steel/chemfuel industry and storage layout without forcing combat; 40 shells consume 600 steel and 600 chemfuel before infrastructure. Machining table is currently blueprint-ungated. Check shell recipe skill and secure sufficient fuel; Biofuel refining must also be completed if local refining is the guaranteed supply route. |
| 38 | **Bionic Workshop:** locally fabricate 2 bionic arms and 2 bionic legs, retain all four, and maintain a powered fabrication workshop. Minimum day 35. | **Power + Research**; **Bionics + Fabrication** and recipe prerequisites. | Advanced manufacturing candidate, offered only after tech completion and guaranteed access to plasteel/components. Manufacturing is the objective; installation is optional, so no surgeon, injury or risky elective operation is required. |
| 39 | **Brewery Settlement:** harvest 300 hops locally, brew/ferment 200 beer, and retain 100 in protected storage; maintain a temperature-controlled fermentation room. Minimum day 25. | **Climate + Power + Research**; **Beer brewing**. | Adds farming, brewing, hauling and controlled fermentation instead of another meal quota. Brewery/barrels are currently outside blueprint groups, but eligibility still requires drafted groups. Check hops, fermentation temperatures and ideology restrictions; no compulsory drinking. |
| 40 | **Geothermal Station:** build a geothermal generator over a usable geyser and use it to power an indoor farm of 6 basins plus a freezer for 7 days; harvest 600 rice from the basins. Minimum day 30. | **Power + Farming + Climate**; **Geothermal power + Hydroponics**. | Creates a settlement anchored to terrain. Geothermal generator is currently outside the Power group; do not imply the card directly unlocks it. Guarantee a reachable buildable geyser and a feasible power budget before offering. |

### Offering these objectives

- Filter on **reward-owned blueprint groups and completed research at the moment choices are presented**. Many node presets are assigned when the act graph is generated; a static eligibility snapshot then would miss rewards picked later. Keep the destination identifiable while choosing its compatible objective when it becomes an offered choice, or refresh still-unoffered assignments.
- Preview the actual locks, e.g. “Requires Kitchen + Power drafted, Nutrient paste completed.” Once offered, store the chosen objective and scaled target rather than silently replacing it when the menu reopens.
- Check pawn abilities and recipe skills as well as technology. A bench being buildable does not imply anyone can use its recipes.
- Resource-heavy candidates need generated/guaranteed supplies or must be omitted. Components, silver, plasteel, chemfuel, salvage and a usable geyser are not assured on every fresh 150×150 map.
- Research already completed at node entry is an eligibility condition, not progress toward a local production objective. All advanced timing assumes that research is already done.
- Each additional gate narrows the pool. Always preserve enough compatible alternatives; do not force the player to pick an impossible objective or assume a later reward will fix it.

First candidates to prototype: **Paste Commissary, Uniform Workshop, Furnished Retreat, Quartermaster Depot, Renewable Campus**. They make existing blueprint rewards visibly useful with relatively direct completion checks. Hospital, fabrication, bionics and munitions candidates belong later in a run, after their research and material checks pass.

Evidence for unlock names and prerequisite chains was read from the installed 1.7.5014 `ResearchProjectDefs/`; relevant plant sow tags and facility behavior were checked in the local crop/building XML. The quotas and operating periods are proposed balance values, not vanilla requirements or measured durations.

## Progress and validation record

2026-11-21: Added candidates 21–40 tied to drafted blueprint groups and completed research; documented that direct research rewards are not yet implemented. Checked installed project prerequisites, hydroponic crop support, smelter unlock, component recipe and shell material costs; gameplay code remains unchanged.

2026-11-21: Reviewed installed 1.7.5014 XML, actual blueprint groups, objective checks and weather formulas. Expanded to 20 candidates, removed mood victory requirement, separated SMG/rifle tiers, corrected material/research/speed arithmetic, and treated weather as adjustable.

No game was launched, production code edited or temporary artifacts created. Estimates remain unplaytested; verify three versus five pawns, known versus missing research, soil, season and the chosen weather trajectory before treating windows as balance guarantees.
