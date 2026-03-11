# Neuro-Interface Augment

ent-ActionAugmentNeuroInterface = Open Neuro-Interface
    .desc = Open the augmentation control interface.

ent-AugmentNeuroInterface = neuro-interface
    .desc = An implantable neuro-interface unit installed at the neck-back of the head.
ent-AugmentNeuroInterfaceCasing = neuro-interface casing
    .desc = A casing for assembling a neuro-interface implant.
ent-AugmentNeuroInterfaceCables = neuro-interface casing with cables
    .desc = A neuro-interface casing wired for final assembly.
ent-AugmentNeuroInterfaceChip = neuro-interface microchip
    .desc = A control microchip required to assemble a neuro-interface implant.
ent-AugmentModuleNeuroCapacity = neuro-capacity module
    .desc = A dedicated module that increases neuro-interface max neuro-load by 5.

augment-examine-neuro-interface = Neuro-interface (neck)

neuro-interface-window-title = Neuro-interface
neuro-interface-window-code = Code-{$code}
neuro-interface-window-source-none = none
neuro-interface-window-source-multiple = {$count} generators
neuro-interface-window-source-value = Charge source: {$source}
neuro-interface-window-output-value = Output: {$rate} energy/sec
neuro-interface-window-consumption-value = Consumption: {$rate} energy/sec
neuro-interface-window-battery-none = Battery charge: no battery
neuro-interface-window-battery-value = Battery charge: {$current}/{$max} ({$percent}%)
neuro-interface-window-neuro-load-title = Neuro-load
neuro-interface-window-neuro-load-value = {$current}/{$max}
neuro-interface-window-no-augments = No augments detected
neuro-interface-tooltip-description-unknown = No description available.
neuro-interface-tooltip-description = Description: {$description}
neuro-interface-tooltip-section-power-passive = Passive power consumption:
neuro-interface-tooltip-section-power-active = Active power consumption:
neuro-interface-tooltip-section-neuro-passive = Passive neuro-load:
neuro-interface-tooltip-section-neuro-active = Active neuro-load:
neuro-interface-tooltip-metric-line = • {$label}: {$value}
neuro-interface-tooltip-source-neuro-passive = Base load
neuro-interface-tooltip-source-power-movement = Movement system
neuro-interface-tooltip-source-power-item-panel-extend = Item panel (deploy)
neuro-interface-tooltip-source-power-item-panel-retract = Item panel (retract)
neuro-interface-tooltip-source-neuro-item-panel-equipped = Item panel (item equipped)
neuro-interface-tooltip-source-power-vision-passive = Vision subsystem (passive)
neuro-interface-tooltip-source-power-vision-active = Vision subsystem (active)
neuro-interface-tooltip-source-power-vision-night = Night vision mode
neuro-interface-tooltip-source-power-vision-thermal = Thermal vision mode
neuro-interface-tooltip-source-neuro-vision-active = Vision subsystem (active)
neuro-interface-tooltip-source-neuro-vision-night = Night vision mode
neuro-interface-tooltip-source-neuro-vision-thermal = Thermal vision mode
neuro-interface-tooltip-source-power-toggle = Toggle module
neuro-interface-tooltip-source-power-passive-generic = Passive module
neuro-interface-tooltip-source-power-module-passive = Universal module (passive power)
neuro-interface-tooltip-source-neuro-module-passive = Universal module (neuro-load)
neuro-interface-tooltip-source-power-module-vision-active-multiplier = Universal module: Vision active power multiplier (x)
neuro-interface-tooltip-source-power-module-vision-active-delta = Universal module: Vision active power delta
neuro-interface-tooltip-source-power-module-itempanel-active-multiplier = Universal module: ItemPanel active power multiplier (x)
neuro-interface-tooltip-source-power-module-itempanel-active-delta = Universal module: ItemPanel active power delta
neuro-interface-tooltip-source-neuro-module-vision-active-multiplier = Universal module: Vision active neuro-load multiplier (x)
neuro-interface-tooltip-source-neuro-module-vision-active-delta = Universal module: Vision active neuro-load delta
neuro-interface-tooltip-source-neuro-module-itempanel-active-multiplier = Universal module: ItemPanel active neuro-load multiplier (x)
neuro-interface-tooltip-source-neuro-module-itempanel-active-delta = Universal module: ItemPanel active neuro-load delta
neuro-interface-tooltip-source-power-intercept-penalty = Intercept penalty (foreign control)
neuro-interface-tooltip-source-neuro-intercept-penalty = Intercept neuro penalty (foreign control)
neuro-interface-tooltip-source-intercept-penalty-duration = Intercept penalty duration (sec)

neuro-interface-button-enable = Enable
neuro-interface-button-disable = Disable
neuro-interface-button-settings = Settings
neuro-interface-button-disable-implants = Disable impl.
neuro-interface-button-enable-implants = Enable impl.
neuro-interface-button-disable-limbs = Disable limbs
neuro-interface-button-enable-limbs = Enable limbs
neuro-interface-button-disable-all = Disable all
neuro-interface-button-enable-all = Enable all
neuro-interface-quick-actions-label = Quick Actions
neuro-interface-info-label = Information

neuro-interface-popup-cannot-toggle = This augment cannot be toggled.
neuro-interface-popup-emp-blocked = This augment is temporarily disabled by EMP.
neuro-interface-popup-brain-blocked = This augment is force-deactivated due to critical brain damage.
neuro-interface-popup-brain-overload-damage = Your brain is overheating from augmentation overload and taking damage.


neuro-interface-augments-label = Augmentation Control

neuro-interface-status-tooltip-enabled = Implant is active.
neuro-interface-status-tooltip-disabled = Implant is disabled.
neuro-interface-status-tooltip-deactivated = Implant is deactivated.
neuro-interface-status-tooltip-no-power = Implant is inactive: insufficient power.

neuro-interface-modules-label = Modules:
neuro-interface-module-line = {$slot}: {$name}

augment-modules-slot-neuro-interface-capacity = neuro-capacity module slot
augment-modules-slot-neuro-interface-universal = universal module slot
