# ARPlatformer

**ARPlatformer** е Unity-базирана AR игра от тип platformer, която превръща реалното физическо пространство в интерактивно игрово поле. Приложението сканира стаята, изгражда пространствен mesh на средата и използва този mesh като основа за колизии, оклузия и геймплей логика. Резултатът е хибрид между класическа платформена механика и augmented reality, в която подът, мебелите и препятствията от реалния свят стават част от нивото.

---

## Обща концепция

Идеята на проекта е проста, но технически интересна: вместо нивото да бъде предварително моделирано, то се **генерира от реалната стая**. Потребителят първо сканира помещението, след което системата преминава от режим на заснемане към режим на игра. След приключване на сканирането се появява герой, събират се монети, играчът достига целта и нивото приключва.

---

## Основни възможности

- Сканиране на реална стая в AR режим
- Генериране на mesh на околната среда
- Използване на mesh-а за колизии и оклузия
- Спаунване на играча върху сканираното пространство
- Платформер movement с ходене, скачане и respawn
- Събиране на монети и достигане на целта
- Touch управление чрез on-screen joystick
- Бутон за рестарт на сесията
- Динамично превключване между scan phase и gameplay phase

---

## Как работи pipeline-ът

ARPlatformer преминава през няколко ясно разграничени етапа:

### 1. Boot / Initialization
При стартиране приложението подготвя AR сесията, зарежда runtime обектите и подава към системата сценичните шаблони за играча, монетите, целта и останалите елементи.

### 2. Scan phase
Потребителят започва сканиране на помещението. В този режим системата:
- събира данни от AR камерата;
- изгражда mesh на средата;
- визуализира mesh-а в preview режим;
- подсказва на потребителя да обхване пода и горните повърхности на препятствията.

### 3. Finalization
След натискане на **Finish Scan** сканирането спира и mesh-ът се заключва за използване в gameplay режим. В този момент се активира оклузията и сцената преминава към по-стабилно поведение за игра.

### 4. Gameplay phase
След приключване на сканирането се създава игровият слой:
- спаунва се player character;
- поставят се монети;
- генерира се целеви флаг;
- активира се управление чрез joystick;
- започва проверка за събиране на всички монети и достигане на целта.

### 5. Completion / Reset
При изпълнение на условията за победа играта завършва. При нужда може да се рестартира цялата сесия чрез **Reset Session**.

---

## Архитектура на проекта

Проектът е организиран около няколко основни скрипта.

### `ARPlatformerRuntime`
Това е главният orchestrator на приложението. Той управлява:
- състоянията на сесията;
- стартиране и спиране на сканирането;
- превключването между AR scan и gameplay режим;
- UI логиката;
- обновяването на mesh и игровите обекти.

### `ARPlatformerGameplaySession`
Този модул управлява самата игра:
- спаун на играча;
- позициониране на монети и цел;
- обработка на input;
- проверка за събиране на предмети;
- условие за завършване на нивото;
- checkpoint и respawn логика.

### `ARPlatformerContentFactory`
Отговаря за създаването на content обектите:
- player prefab;
- coin prefab;
- goal flag;
- fallback primitives, когато липсва шаблон;
- подреждане на runtime обектите под общ root.

### `PlatformerCharacterController`
Реализира движението на героя:
- хоризонтално придвижване;
- jump механика;
- гравитация;
- проверка за падане извън допустимата зона;
- respawn поведение чрез checkpoint.

### `TouchJoystick`
UI компонент за мобилно управление:
- следи drag жестове;
- преобразува touch input в нормализиран `Vector2`;
- подава вход към player controller-а.

---

## Дизайн на игровия поток

Геймплей лууп-а е структуриран така, че да бъде разбираем за потребителя и стабилен за AR среда:

1. Стартиране на приложението
2. Сканиране на реалната стая
3. Генериране и визуализация на mesh
4. Финализиране на сканирането
5. Преминаване към gameplay режим
6. Поява на играча
7. Управление чрез touch joystick
8. Събиране на монети
9. Достигане на флага
10. Завършване на нивото или рестарт

---

## Управление

### Управление в игра
- **Joystick** — движение на героя
- **Jump** — скок
- **Respawn** — връщане към последния checkpoint
- **Reset Session** — рестартиране на AR сесията

### Сканиране
- **Finish Scan** — прекратява заснемането и преминава към игра

---

## Технически бележки

Кодът показва ясна практическа ориентация към AR gameplay, но има и някои типични инженерни компромиси, които са очаквани в такъв проект:

- `ARPlatformerRuntime` съчетава няколко различни отговорности и би могъл да бъде разделен на по-малки компоненти.
- Runtime lookup-ите към сценични обекти са функционални, но не са идеални от гледна точка на мащабируемост.
- Генерирането на fallback primitives е полезно за устойчивост, но в продукционна среда е по-добре да се разчита на подготвени prefabs.
- Mesh update-ите и физиката трябва да се следят внимателно за производителност, особено на мобилни устройства.

Въпреки това архитектурата е достатъчно ясна и логична за прототип или техническо демо.

---

## Изисквания

- iOS мобилно устройсто с LiDAR сензори (тествано само на iOS устройства)
- Възможност за достъп до камерата
- Активирани нужните AR/SDK зависимости за платформата
- Инсталирани проектни ресурси и prefabs

---

## Технологичен стек и зависимости

- **Unity** (проектът е структуриран за мобилен AR runtime)
- **Vuforia Engine** (Area Target Capture + runtime Image Target)
- **Unity Input System** (touch joystick + UI input)
- **URP/Standard shader fallback логика** за визуализация на scan mesh

> Забележка: при промяна на Unity/Vuforia версии първо валидирайте scan pipeline-а и runtime mesh collider поведението на реално устройство.

---

## Сценични зависимости (важно)

За да работи gameplay слоят предвидимо, сцената трябва да съдържа:

- `AreaTargetCaptureBehaviour` (за scan phase)
- runtime обект с `ARPlatformerRuntime`
- (по избор) шаблонен root **AR Platformer Templates** със следните деца:
  - `Coin Template`
  - `Goal Flag Template`
  - `Course Block Template`
  - `Tall Course Block Template`
  - `Checkpoint Template`

Ако template-ите липсват, системата преминава към fallback primitives и логва предупреждения в Console.

---

## Runtime конфигурация (ARPlatformerRuntimeConfig)

Най-важните параметри за стабилност и качество на gameplay:

- **Spawn / Surface**
  - `respawnRayHeight`, `respawnRayDistance`
  - `surfaceHoverHeight`
  - `environmentRaycastMask`
- **Physics layers**
  - `playerPhysicsLayer`
  - `spatialMeshPhysicsLayer`
- **Spawn readiness**
  - `markerSpawnDelaySeconds`
  - `markerSpawnStableChecks`
  - `markerSpawnReadinessTimeoutSeconds`
- **Layout**
  - `surfaceSampleSpacing`, `minSurfaceSpacing`
  - `coinCountTarget`, `coursePropTarget`
  - `minGoalDistance`, `minCoinGoalDistance`
- **Character movement**
  - `characterMoveSpeed`, `characterJumpHeight`, `characterGravity`

### Критична бележка за Physics Matrix

`playerPhysicsLayer` и `spatialMeshPhysicsLayer` трябва да имат активна колизия в **Project Settings > Physics**.  
Runtime-ът вече има auto-correct защита, но правилната матрица в проекта остава препоръчителната конфигурация.

---

## Начин на използване

1. Отворете проекта в Unity.
2. Стартирайте приложението на AR устройство.
3. Сканирайте стаята, като обхванете пода и горните повърхности на основните обекти.
4. Натиснете **Finish Scan**, когато средата е достатъчно картографирана.
5. Изчакайте играта да премине в режим на gameplay.
6. Управлявайте героя чрез joystick-а.
7. Съберете всички монети и достигнете флага, за да завършите нивото.

---

## QA чеклист (бърза валидация)

Използвайте този минимум след промени по runtime, physics или layout генерацията:

1. **Scan quality check**  
   Сканирайте пода + горните повърхности на мебелите; натиснете `Finish Scan`.
2. **Spawn stability**  
   Играчът трябва да се появи стабилно върху валидна повърхност без мигновен respawn loop.
3. **Collision check**  
   При движение персонажът не трябва да пропада през scan mesh.
4. **Layout generation**  
   Трябва да има goal flag и coins (или fallback варианти при непълни templates/scan).
5. **Gameplay completion**  
   Събиране на всички монети + достигане на флага трябва да завърши нивото.
6. **Recovery actions**  
   `Respawn` и `Reset Session` трябва да работят без блокиране на състоянията.

---

## Troubleshooting

### 1) Играчът пада през пода

Проверете последователно:

- дали `playerPhysicsLayer` и `spatialMeshPhysicsLayer` collide-ват в Physics Matrix;
- дали runtime mesh collider-ите съществуват и са на правилния layer;
- дали scan-ът покрива достатъчно добре walkable зони;
- дали `fallRespawnDistance` (в `PlatformerCharacterController`) не е твърде нисък в Inspector.

### 2) Не се появяват монети/флаг

- проверете Console за warnings от `ARPlatformerContentFactory` (липсващи templates);
- проверете `environmentRaycastMask` и слоевете на scan mesh;
- в sparse scan условия fallback позициите трябва да се генерират; ако не, проверете scan coverage и raycast distance параметрите.

### 3) Marker е открит, но spawn не започва

- валидирайте camera permissions;
- проверете дали Image Target текстурата се зарежда от `StreamingAssets/Markers/platformer-marker.png`;
- проверете readiness timeout логове и дали scan mesh има валидна геометрия.

---

## Производителност и устойчивост

- Collider sync е throttled през `scanCollisionSyncInterval`.
- Runtime използва fallback визуални обекти при липсващи templates.
- UI refresh е интервално управление (`uiRefreshInterval`) за по-стабилен мобилен runtime.
- При разширяване на проекта с повече сцени, имайте предвид че `Physics.IgnoreLayerCollision` променя глобално physics състоянието в рамките на сесията.

---

## Кратка оценка (Evaluation)

Този раздел обобщава текущото състояние на проекта като студентска разработка и прототип за реален продукт.

### Резултат от изпълнение

- Основният pipeline работи end-to-end: `scan -> marker -> spawn -> collect -> goal -> complete`.
- Реализирани са ключови AR зависимости: runtime mesh, колизии, оклузия и позициониране в реална среда.
- Добавени са защитни механизми за чести runtime проблеми:
  - fallback content при липсващи templates;
  - spawn readiness проверки;
  - диагностика за layer collision конфигурация;
  - стабилизирана respawn логика.

### Качество и инженерни практики

- Архитектурата е модулна и четима (`ARPlatformerRuntime`, `ARPlatformerGameplaySession`, `ARPlatformerContentFactory`).
- Конфигурируемостта е добра чрез `ARPlatformerRuntimeConfig`.
- Документацията е достатъчна за onboarding и QA smoke валидация.

### Ограничения в текущата версия

- Липсва автоматизирано тестово покритие (playmode/regression тестове).
- Поведението зависи от качеството на сканиране, осветлението и геометрията на помещението.
- Няма интегрирана analytics/crash telemetry стратегия за production мониторинг.

---

## Снимки от физически устройства (iPhone 17 Pro Max/iPhone 14 Pro)

### Preparation
<img width="942" height="2046" alt="2df53b91-37ab-4d47-b973-86586ab95b38" src="https://github.com/user-attachments/assets/42ba66a6-7096-485e-8687-aa1fcc4d2580" />

### Scan mode
<img width="942" height="2046" alt="ac1f2ae8-1e65-4462-9582-e778b0072c5f" src="https://github.com/user-attachments/assets/19d2bbc6-5d2c-40f0-a8c0-33615c89b874" />

### Image Target
<img width="942" height="2046" alt="9c80cd34-0884-4dc7-9729-03c6eb7e900c" src="https://github.com/user-attachments/assets/153d3aea-71c0-41e0-804e-9f5d6453532c" />

### Gameplay mode
<img width="942" height="2046" alt="ba24960b-0bf4-48c0-82bc-6fe794c46d39" src="https://github.com/user-attachments/assets/70c01f5b-1dd4-4e8d-a67a-819afbd5cbcc" />

---

## Възможности за развитие

ARPlatformer може да бъде разширен в няколко посоки:

- по-прецизна класификация на повърхностите;
- по-интелигентно разположение на платформите и препятствията;
- object pooling за монети и декоративни елементи;
- по-добра визуална обратна връзка при събиране на обекти;
- допълнителни нива и цели;
- подобрено mobile UX;
- мултиплейър AR режим;
- по-детайлна occlusion система.

---

## Заключение

ARPlatformer е AR експеримент, който съчетава пространствено сканиране, runtime mesh обработка и класическа platformer логика. Най-силната му страна е именно в това, че не симулира средата, а използва реалната среда като част от геймплея.
