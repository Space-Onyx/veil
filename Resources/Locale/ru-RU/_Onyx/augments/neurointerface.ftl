# Neuro-Interface Augment

ent-ActionAugmentNeuroInterface = Открыть нейро-интерфейс
    .desc = Открыть интерфейс управления аугментациями.

ent-AugmentNeuroInterface = NanoTrasen Нейро
    .desc = Имплантируемый нейро-интерфейс, устанавливаемый в область шеи.
ent-AugmentNeuroInterfaceInterdyneFault = Interdyne Фаулт
    .desc = Специализированный нейро-интерфейс Interdyne. Имеет увеличенный базовый лимит нейро-нагрузки.
ent-AugmentNeuroInterfaceNanoTrasenSilovik = NanoTrasen Силовик
    .desc = Усиленный нейро-интерфейс NanoTrasen с повышенным лимитом нейро-нагрузки и усиленной ACI-защитой.
ent-AugmentNeuroInterfaceCasing = каркас нейро-интерфейса
    .desc = Каркас для сборки импланта нейро-интерфейса.
ent-AugmentNeuroInterfaceCables = каркас нейро-интерфейса с кабелями
    .desc = Каркас нейро-интерфейса, подготовленный к финальной сборке.
ent-AugmentNeuroInterfaceChip = микросхема нейро-интерфейса
    .desc = Управляющая микросхема, необходимая для сборки импланта нейро-интерфейса.
ent-AugmentModuleNeuroCapacity = модуль расширения нейро-лимита
    .desc = Специализированный модуль, увеличивающий максимум нейро-нагрузки нейро-интерфейса на 5.

augment-examine-neuro-interface = Нейро-интерфейс (шея)

neuro-interface-window-title = Нейро-интерфейс
neuro-interface-window-code = ID-{$code}
neuro-interface-window-source-none = Отсутствует.
neuro-interface-window-source-multiple = {$count} источников
neuro-interface-window-source-value = Источник: {$source}
neuro-interface-window-output-value = Выработка: {$rate} ед/сек
neuro-interface-window-consumption-value = Потребление: {$rate} ед/сек
neuro-interface-window-battery-none = Заряд батареи: нет батареи
neuro-interface-window-battery-value = Заряд батареи: {$current}/{$max} ({$percent}%)
neuro-interface-window-neuro-load-title = Нейро-нагрузка
neuro-interface-window-neuro-load-value = {$current}/{$max}
neuro-interface-window-ram-title = Оперативная память
neuro-interface-window-ram-value = {$current}/{$max}
neuro-interface-window-no-augments = Аугментации не обнаружены
neuro-interface-tooltip-description-unknown = Описание недоступно.
neuro-interface-tooltip-description = Описание: {$description}
neuro-interface-tooltip-section-power-passive = Пассивное потребление энергии:
neuro-interface-tooltip-section-power-active = Активное потребление энергии:
neuro-interface-tooltip-section-neuro-passive = Пассивная нейро-нагрузка:
neuro-interface-tooltip-section-neuro-active = Активная нейро-нагрузка:
neuro-interface-tooltip-metric-line = • {$label}: {$value}
neuro-interface-tooltip-source-neuro-passive = Базовая нагрузка
neuro-interface-tooltip-source-power-movement = Система передвижения
neuro-interface-tooltip-source-power-item-panel-extend = Панель предмета (извлечение)
neuro-interface-tooltip-source-power-item-panel-retract = Панель предмета (втягивание)
neuro-interface-tooltip-source-neuro-item-panel-equipped = Панель предмета (предмет извлечён)
neuro-interface-tooltip-source-power-vision-passive = Подсистема зрения (пассив)
neuro-interface-tooltip-source-power-vision-active = Подсистема зрения (актив)
neuro-interface-tooltip-source-power-vision-night = Режим ночного зрения
neuro-interface-tooltip-source-power-vision-thermal = Режим теплового зрения
neuro-interface-tooltip-source-neuro-vision-active = Подсистема зрения (актив)
neuro-interface-tooltip-source-neuro-vision-night = Режим ночного зрения
neuro-interface-tooltip-source-neuro-vision-thermal = Режим теплового зрения
neuro-interface-tooltip-source-power-toggle = Переключаемый модуль
neuro-interface-tooltip-source-power-passive-generic = Пассивный модуль
neuro-interface-tooltip-source-power-module-passive = Универсальный модуль (пассивное питание)
neuro-interface-tooltip-source-neuro-module-passive = Универсальный модуль (нейро-нагрузка)
neuro-interface-tooltip-source-power-module-vision-active-multiplier = Универсальный модуль: множитель активной энергии Vision (x)
neuro-interface-tooltip-source-power-module-vision-active-delta = Универсальный модуль: прибавка активной энергии Vision
neuro-interface-tooltip-source-power-module-itempanel-active-multiplier = Универсальный модуль: множитель активной энергии ItemPanel (x)
neuro-interface-tooltip-source-power-module-itempanel-active-delta = Универсальный модуль: прибавка активной энергии ItemPanel
neuro-interface-tooltip-source-neuro-module-vision-active-multiplier = Универсальный модуль: множитель активной нейро-нагрузки Vision (x)
neuro-interface-tooltip-source-neuro-module-vision-active-delta = Универсальный модуль: прибавка активной нейро-нагрузки Vision
neuro-interface-tooltip-source-neuro-module-itempanel-active-multiplier = Универсальный модуль: множитель активной нейро-нагрузки ItemPanel (x)
neuro-interface-tooltip-source-neuro-module-itempanel-active-delta = Универсальный модуль: прибавка активной нейро-нагрузки ItemPanel
neuro-interface-tooltip-source-power-intercept-penalty = Штраф перехвата (чужой интерфейс)
neuro-interface-tooltip-source-neuro-intercept-penalty = Нейро-штраф перехвата (чужой интерфейс)
neuro-interface-tooltip-source-intercept-penalty-duration = Длительность штрафа перехвата (сек)

neuro-interface-button-enable = Включить
neuro-interface-button-disable = Отключить
neuro-interface-button-settings = Настройки
neuro-interface-button-disable-implants = Откл. импланты
neuro-interface-button-enable-implants = Вкл. импланты
neuro-interface-button-disable-limbs = Откл. конечности
neuro-interface-button-enable-limbs = Вкл. конечности
neuro-interface-button-disable-all = Откл. всё
neuro-interface-button-enable-all = Вкл. всё
neuro-interface-quick-actions-label = Быстрые действия
neuro-interface-info-label = Информация

neuro-interface-popup-cannot-toggle = Эту аугментацию нельзя переключать.
neuro-interface-popup-emp-blocked = Аугментация временно отключена ЭМИ.
neuro-interface-popup-brain-blocked = Аугментация принудительно деактивирована из-за критического повреждения мозга.
neuro-interface-popup-brain-overload-damage = Ваш разум начинает плыть. Мозг перегружен!

neuro-interface-augments-label = Управление аугментациями

neuro-interface-status-tooltip-enabled = Аугмент активен.
neuro-interface-status-tooltip-disabled = Аугмент выключен.
neuro-interface-status-tooltip-deactivated = Аугмент деактивирован.
neuro-interface-status-tooltip-no-power = Аугмент не активен, отсутствует питание.

neuro-interface-modules-label = Модули:
neuro-interface-module-line = {$slot}: {$name}

augment-modules-slot-neuro-interface-capacity = нейро-лимит
augment-modules-slot-neuro-interface-universal = универсальный слот модуля
augment-modules-slot-neuro-interface-cyberdeck = слот модуля кибердеки
augment-modules-slot-cyberdeck-program = слот программы
augment-modules-slot-cyberdeck-ram = слот ОЗУ

ent-AugmentModuleCyberDeck = NanoTrasen «Пульсар»
    .desc = Кибердека для нейро-интерфейса. Предоставляет слоты для программных модулей.
ent-AugmentModuleCyberDeckInterdyne = Interdyne «Падшие»
    .desc = Кибердека для нейро-интерфейса. Предоставляет слоты для программных модулей.

ent-ActionCyberDeckScriptImplantFailure = Сбой имплантов
    .desc = Временно деактивирует все видимые импланты и кибернетические конечности.
ent-AugmentModuleCyberDeckScriptImplantFailure = модуль сбоя имплантов
    .desc = Скрипт кибердеки, деактивирующий видимые импланты и кибер-конечности на 6-8 секунд. Стоимость: 8 ОЗУ за использование.
ent-ActionCyberDeckScriptRemoteDeactivation = Удалённая деактивация
    .desc = Удалённо открывает или закрывает шлюз в радиусе действия.
ent-AugmentModuleCyberDeckScriptRemoteDeactivation = модуль удалённой деактивации
    .desc = Скрипт кибердеки, удалённо открывающий и закрывающий шлюзы. Стоимость: 2 ОЗУ за использование.
ent-ActionCyberDeckScriptOpticsOverload = Перегрузка оптики
    .desc = Временно отключает кибернетическую оптику выбранной цели.
ent-AugmentModuleCyberDeckScriptOpticsOverload = модуль перегрузки оптики
    .desc = Скрипт кибердеки, отключающий кибернетические глаза на 5-6 секунд. Стоимость: 6 ОЗУ за использование.

cyberdeck-script-not-enough-ram = Недостаточно ОЗУ для выполнения скрипта!
cyberdeck-script-aci-blocked = Защита ACI слишком высокая для этого скрипта.
