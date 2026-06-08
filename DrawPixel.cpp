#include "DxLib.h"
#include <vector>
#include <cmath>

// Constants
const int SCREEN_WIDTH = 640;
const int SCREEN_HEIGHT = 480;
const int WINDOW_WIDTH = 1280;
const int WINDOW_HEIGHT = 720;
const float GRAVITY = 0.5f;
const int MAX_BULLETS = 40;
const float BULLET_SPEED = 20.0f;
const float JUMP_POWER = -12.0f;
const float WALK_SPEED = 4.0f;
const float DASH_SPEED = 8.0f;

//敵Constants
const float ENEMY_WALK_SPEED = 2.0f;
const float ENEMY_ATTACK_RANGE_X = 300.0f;
const float ENEMY_ATTACK_RANGE_Y = 100.0f;
const float ENEMY_ATTACK_COOLDOWN = 60.0f;

enum EnemyState {
    PATROL,
    ATTACK
};

//ゲームシーン
enum GameScene {
    PLAY,
    RESULT_GAMEOVER,
    RESULT_VICTORY
};

// Structures
struct Player {
    float x, y;
    float vx, vy;
    int handle;
    int direction;
    bool isJumping;
    int width, height;
    float scale;
    float angle;
    float speedScale;
    int hp;
};

//敵Structures
struct Enemy {
    float x, y;
    float vx, vy;
    int handle;
    int direction;
    int width, height;
    float scale;
    EnemyState state;
    float patrolLeft, patrolRight;
    float attackTimer;
    int hp;
};

//感圧板Structures
struct PressurePlate {
    float x, y, width, height;
    bool isPressed;
    float requiredScale;
};

//ちくわブロックStructures
struct ChikuwaBlock {
    float x, y, width, height;
    float originalY;
    float rideTimer;
    float fallDelay;
    bool isFalling;
    float vy;
    bool isPlayerOn;
};

//反・一時停止装置Structures
struct TimeField {
    float x, y;
    float radius;
};

// 敵B（即死の敵食いギミック）Structures
struct Chomper {
    float x, y;
    float width, height;
    bool isActive;
};

struct CutPoint {
    float timePos;
    float targetTimePos;
};

struct Bullet {
    float x, y, vx;
    bool isActive;
    int handle;
    float width, height;
};

struct ContextMenu {
    bool isOpen;
    int x, y, width, height;
};

bool CheckCollision(float x1, float y1, float w1, float h1, float x2, float y2, float w2, float h2) {
    return (x1 < x2 + w2 && x1 + w1 > x2 && y1 < y2 + h2 && y1 + h1 > y2);
}

int WINAPI WinMain(HINSTANCE h, HINSTANCE hp, LPSTR l, int n)
{
    ChangeWindowMode(TRUE);
    SetGraphMode(WINDOW_WIDTH, WINDOW_HEIGHT, 32);
    if (DxLib_Init() == -1) return -1;
    SetDrawScreen(DX_SCREEN_BACK);

    int gameScreen = MakeScreen(SCREEN_WIDTH, SCREEN_HEIGHT, TRUE);
    int playerHandle = LoadGraph("img/player.png");
    int bulletHandle = LoadGraph("img/bullet.png");
    int yukaHandle = LoadGraph("img/yuka.png");
    int enemyHandle = LoadGraph("img/enemy.png");
    int enemyBulletHandle = LoadGraph("img/enemyBullet.png");

    int pw, ph;
    GetGraphSize(playerHandle, &pw, &ph);

    int ew, eh;
    GetGraphSize(enemyHandle, &ew, &eh);

    Player player = { 50.0f, 300.0f, 0.0f, 0.0f, playerHandle, 0, false, pw, ph, 1.0f, 0.0f, 1.0f, 3 };

    // 初期化：敵A
    Enemy enemy = { 450.0f, 300.0f, 0.0f, 0.0f, enemyHandle, 1, ew, eh, 1.0f, PATROL, 400.0f, 550.0f, 0.0f, 3 };
    // 初期化：感圧板
    PressurePlate plate = { 220.0f, 390.0f, 60.0f, 10.0f, false, 1.5f };
    // 初期化：ちくわブロック
    ChikuwaBlock chikuwa = { 360.0f, 280.0f, 80.0f, 15.0f, 280.0f, 0.0f, 45.0f, false, 0.0f, false };
    // 初期化：反・一時停止装置
    TimeField tField = { 320.0f, 240.0f, 100.0f };

    // 初期化：敵B
    Chomper chomper = { 260.0f, 400.0f - 50.0f, 40.0f, 50.0f, true };

    std::vector<CutPoint> cuts;
    std::vector<Bullet> bullets(MAX_BULLETS);
    std::vector<Bullet> enemyBullets(MAX_BULLETS);

    for (int i = 0; i < MAX_BULLETS; i++) {
        bullets[i].isActive = false; bullets[i].handle = bulletHandle; bullets[i].width = 16.0f; bullets[i].height = 16.0f;
        enemyBullets[i].isActive = false; enemyBullets[i].handle = enemyBulletHandle; enemyBullets[i].width = 16.0f; enemyBullets[i].height = 16.0f;
    }

    
    float groundY = 400.0f;
    bool isPaused = false, isEditMode = false, isFastForward = false, isStepFrame = false;
    bool isDragging = false, isScaling = false, isRotating = false, isDraggingField = false;
    bool isInspScale = false, isInspAngle = false, isInspSpeed = false, isInspChikuwa = false;//変更有り
    float dragOffsetX = 0, dragOffsetY = 0, dragFieldOffsetX = 0, dragFieldOffsetY = 0;//変更有り
    float baseScale = 1.0f, baseAngle = 0.0f, baseSpeed = 1.0f, baseChikuwaDelay = 45.0f;//変更有り
    int lastMouseX = 0, lastMouseY = 0;
    float tempCutStart = -1.0f, globalTimeScale = 1.0f;
    ContextMenu menu = { false, 0, 0, 160, 120 };
    SetMouseDispFlag(TRUE);

    int monitorX = (WINDOW_WIDTH - SCREEN_WIDTH) / 2;
    int monitorY = (WINDOW_HEIGHT - SCREEN_HEIGHT) / 2 - 40;

    //ゲームシーンの設定：プレイ画面
    GameScene currentScene = PLAY;

    //リセットボタンを押されたら
    auto ResetGame = [&]() {
        player.x = 50.0f; player.y = 300.0f; player.vx = 0; player.vy = 0; player.direction = 0;
        player.scale = 1.0f; player.angle = 0.0f; player.speedScale = 1.0f; player.hp = 3; player.isJumping = false;

        enemy.x = 450.0f; enemy.y = 300.0f; enemy.vx = 0; enemy.vy = 0; enemy.direction = 1;
        enemy.state = PATROL; enemy.attackTimer = 0.0f; enemy.hp = 3; enemy.scale = 1.0f;

        for (int i = 0; i < MAX_BULLETS; i++) { bullets[i].isActive = false; enemyBullets[i].isActive = false; }

        plate.isPressed = false;
        chikuwa.y = chikuwa.originalY; chikuwa.isFalling = false; chikuwa.rideTimer = 0.0f; chikuwa.vy = 0;

        //敵Bの復活
        chomper.isActive = true;

        currentScene = PLAY;
        };

    while (ProcessMessage() == 0 && CheckHitKey(KEY_INPUT_ESCAPE) == 0)
    {
        int mx, my;
        GetMousePoint(&mx, &my);
        float gx, gy;
        if (isEditMode) {
            gx = (float)(mx - monitorX);
            gy = (float)(my - monitorY);
        }
        else {
            gx = (float)mx * ((float)SCREEN_WIDTH / WINDOW_WIDTH);
            gy = (float)my * ((float)SCREEN_HEIGHT / WINDOW_HEIGHT);
        }
        
        static bool lastMiddleClick = false;
        bool currentMiddleClick = (GetMouseInput() & MOUSE_INPUT_MIDDLE) != 0;
        if (currentMiddleClick && !lastMiddleClick) { isEditMode = !isEditMode; menu.isOpen = false; }
        lastMiddleClick = currentMiddleClick;

        static bool lastPauseKey = false;
        if (CheckHitKey(KEY_INPUT_SPACE) && !lastPauseKey) isPaused = !isPaused;
        lastPauseKey = (CheckHitKey(KEY_INPUT_SPACE) != 0);

        static bool lastFFKey = false;
        if (CheckHitKey(KEY_INPUT_F) && !lastFFKey) isFastForward = !isFastForward;
        lastFFKey = (CheckHitKey(KEY_INPUT_F) != 0);

        static bool lastStepKey = false;
        isStepFrame = (isEditMode && isPaused && CheckHitKey(KEY_INPUT_RIGHT) && !lastStepKey);
        lastStepKey = (CheckHitKey(KEY_INPUT_RIGHT) != 0);

        static bool lastLeftClick = false;
        bool currentLeftClick = (GetMouseInput() & MOUSE_INPUT_LEFT) != 0;
        static bool lastRightClick = false;
        bool currentRightClick = (GetMouseInput() & MOUSE_INPUT_RIGHT) != 0;

        globalTimeScale = isFastForward ? 2.0f : 1.0f;
        float rangeSpeed = (player.x > SCREEN_WIDTH / 2) ? 1.5f : 1.0f;
        float finalTimeScale = globalTimeScale * rangeSpeed * player.speedScale;

        //ゲームシーン
        if (currentScene == PLAY) {
            //ゲームオバー条件
            if (player.hp <= 0) currentScene = RESULT_GAMEOVER;
            //Victory条件は画面右端到達になっている
            else if (player.x >= SCREEN_WIDTH) currentScene = RESULT_VICTORY;
        }

        bool uiHandled = false;//変更あり

        if (isEditMode) {
            if (currentRightClick && !lastRightClick) { menu.isOpen = true; menu.x = mx; menu.y = my; }

            if (currentLeftClick && !lastLeftClick && menu.isOpen) {
                uiHandled = true;//変更あり
                if (mx >= menu.x && mx <= menu.x + menu.width) {
                    if (my >= menu.y + 5 && my <= menu.y + 30) {
                        if (gx >= player.x && gx <= player.x + player.width * player.scale && gy >= player.y && gy <= player.y + player.height * player.scale) player.speedScale += 0.5f;
                        menu.isOpen = false;
                    }
                    else if (my >= menu.y + 31 && my <= menu.y + 55) {
                        if (gx >= player.x && gx <= player.x + player.width * player.scale && gy >= player.y && gy <= player.y + player.height * player.scale) { player.speedScale -= 0.5f; if (player.speedScale < 0) player.speedScale = 0; }
                        menu.isOpen = false;
                    }
                    else if (my >= menu.y + 56 && my <= menu.y + 80) {
                        player.direction = (player.direction == 0 ? 1 : 0);
                        menu.isOpen = false;
                    }
                    else if (my >= menu.y + 81 && my <= menu.y + 115) {
                        ResetGame();
                        menu.isOpen = false;
                    }
                }
                else { menu.isOpen = false; }
            }

            if (currentLeftClick && !menu.isOpen) {
                if (!lastLeftClick) {
                    if (mx >= WINDOW_WIDTH / 2 - 50 && mx <= WINDOW_WIDTH / 2 + 50 && my >= WINDOW_HEIGHT - 90 && my <= WINDOW_HEIGHT - 70) {
                        isPaused = !isPaused; uiHandled = true;//変更あり
                    }
                    else if (mx >= WINDOW_WIDTH - 240) {
                        uiHandled = true;//変更あり
                        if (my >= 45 && my <= 65) { isInspScale = true; lastMouseX = mx; baseScale = player.scale; }
                        else if (my >= 66 && my <= 85) { isInspAngle = true; lastMouseX = mx; baseAngle = player.angle; }
                        else if (my >= 86 && my <= 105) { isInspSpeed = true; lastMouseX = mx; baseSpeed = player.speedScale; }
                        else if (my >= 115 && my <= 135) { isInspChikuwa = true; lastMouseX = mx; baseChikuwaDelay = chikuwa.fallDelay; }//ちくわブロックスライダー
                    }
                    else if (my >= WINDOW_HEIGHT - 60 && my <= WINDOW_HEIGHT - 20) {
                        uiHandled = true;//変更あり
                        if (CheckHitKey(KEY_INPUT_LCONTROL)) {
                            float ct = (float)(mx - 50) / (WINDOW_WIDTH - 100);
                            if (tempCutStart < 0) tempCutStart = ct;
                            else { cuts.push_back({ (ct > tempCutStart ? tempCutStart : ct), (ct > tempCutStart ? ct : tempCutStart) }); tempCutStart = -1.0f; }
                        }
                    }
                    //変更あり
                    else if (mx < monitorX || mx > monitorX + SCREEN_WIDTH || my < monitorY || my > monitorY + SCREEN_HEIGHT) {
                        uiHandled = true;
                    }
                }

                if (!lastLeftClick && !isDragging && !isScaling && !isRotating && !isDraggingField && !uiHandled) {//変更あり(&& !isDraggingField && !uiHandled)
                    //装置関連
                    float dx = gx - tField.x;
                    float dy = gy - tField.y;
                    if (dx * dx + dy * dy <= tField.radius * tField.radius) {
                        uiHandled = true;
                        if (!CheckHitKey(KEY_INPUT_S) && !CheckHitKey(KEY_INPUT_R)) {
                            isDraggingField = true;
                            dragFieldOffsetX = gx - tField.x;
                            dragFieldOffsetY = gy - tField.y;
                        }
                    }
                    //プレイヤー関連
                    else if (gx >= player.x && gx <= player.x + player.width * player.scale && gy >= player.y && gy <= player.y + player.height * player.scale) {
                        uiHandled = true;
                        if (CheckHitKey(KEY_INPUT_S)) { isScaling = true; lastMouseY = my; baseScale = player.scale; }
                        else if (CheckHitKey(KEY_INPUT_R)) { isRotating = true; lastMouseX = mx; baseAngle = player.angle; }
                        else { isDragging = true; dragOffsetX = gx - player.x; dragOffsetY = gy - player.y; }
                    }
                }

                if (isDragging) { player.x = gx - dragOffsetX; player.y = gy - dragOffsetY; player.vx = 0; player.vy = 0; }
                //装置移動関連
                if (isDraggingField) { tField.x = gx - dragFieldOffsetX; tField.y = gy - dragFieldOffsetY; }
                if (isScaling) { player.scale = baseScale + (float)(lastMouseY - my) * 0.01f; if (player.scale < 0.1f) player.scale = 0.1f; }
                if (isRotating) { player.angle = baseAngle + (float)(mx - lastMouseX) * 0.02f; }

                if (isInspScale) { player.scale = baseScale + (float)(mx - lastMouseX) * 0.01f; if (player.scale < 0.1f) player.scale = 0.1f; }
                if (isInspAngle) { player.angle = baseAngle + (float)(mx - lastMouseX) * 0.02f; }
                if (isInspSpeed) { player.speedScale = baseSpeed + (float)(mx - lastMouseX) * 0.05f; if (player.speedScale < 0) player.speedScale = 0; }
                if (isInspChikuwa) { chikuwa.fallDelay = baseChikuwaDelay + (float)(mx - lastMouseX) * 0.5f; if (chikuwa.fallDelay < 10) chikuwa.fallDelay = 10.0; }
            }
            else {
                isDragging = isScaling = isRotating = isDraggingField = false;//変更あり：(= isDraggingField)
                isInspScale = isInspAngle = isInspSpeed = isInspChikuwa = false;
            }
        }

        //変更あり：ゲーム全体が一時停止されていても、この円の内部にいるオブジェクトだけは時間が進み、動くことができるギミック
        auto CanUpdate = [&](float ox, float oy, float ow, float oh, float scale) {
            if (currentScene != PLAY || isDragging || isScaling || isRotating) return false;
            if (!isPaused || isStepFrame) return true;

            float cx = ox + (ow * scale) / 2.0f;
            float cy = oy + (oh * scale) / 2.0f;
            float dx = cx - tField.x;
            float dy = cy - tField.y;
            return (dx * dx + dy * dy <= tField.radius * tField.radius);
        };
        //プレイヤーの判定（動けるか）
        bool canPlayerAct = CanUpdate(player.x, player.y, player.width, player.height, player.scale);
        static bool lastShot = false;
        //弾を撃つ（Enterキーとマウスクリック）
        bool isEnterShot = (CheckHitKey(KEY_INPUT_RETURN) != 0);
        bool isClickShot = (currentLeftClick && !lastLeftClick && !uiHandled && currentScene == PLAY);
        
        bool currentShot = (isEnterShot || isClickShot) && canPlayerAct;//変更あり：canPlayerAct

        if (currentShot && !lastShot) {
            for (int i = 0; i < MAX_BULLETS; i++) {
                if (!bullets[i].isActive) {
                    bullets[i].isActive = true;
                    bullets[i].x = player.x + (player.direction == 0 ? (float)player.width * player.scale : -10.0f);
                    bullets[i].y = player.y + (float)player.height * player.scale / 4.0f;
                    bullets[i].vx = (player.direction == 0 ? BULLET_SPEED : -BULLET_SPEED);
                    break;
                }
            }
        }
        //プレイヤーが移動できる状況にあるなら
        if (currentScene == PLAY && canPlayerAct) {
            bool isShift = (CheckHitKey(KEY_INPUT_LSHIFT) || CheckHitKey(KEY_INPUT_RSHIFT));
            float speed = isShift ? DASH_SPEED : WALK_SPEED;
            player.vx = 0;
            if (CheckHitKey(KEY_INPUT_A)) { player.vx = -speed; player.direction = 1; }
            if (CheckHitKey(KEY_INPUT_D)) { player.vx = speed; player.direction = 0; }
            if (CheckHitKey(KEY_INPUT_W) && !player.isJumping) { player.vy = JUMP_POWER; player.isJumping = true; }
        }
        else {
            player.vx = 0;
        }

        SetDrawScreen(gameScreen);
        ClearDrawScreen();

        float ts = isStepFrame ? 1.0f : finalTimeScale;
        //条件を変えた
        if (canPlayerAct) {
            player.vy += GRAVITY * ts; player.x += player.vx * ts; player.y += player.vy * ts;
            float tp = player.x / (float)SCREEN_WIDTH;
            for (auto& cp : cuts) {
                if (player.vx > 0 && tp >= cp.timePos && tp < cp.targetTimePos) player.x = cp.targetTimePos * (float)SCREEN_WIDTH;
                else if (player.vx < 0 && tp <= cp.targetTimePos && tp > cp.timePos) player.x = cp.timePos * (float)SCREEN_WIDTH;
            }
        }

        for (int i = 0; i < MAX_BULLETS; i++) {
            if (bullets[i].isActive && CanUpdate(bullets[i].x, bullets[i].y, bullets[i].width, bullets[i].height, 1.0f)) {
                bullets[i].x += bullets[i].vx * ts;
                if (bullets[i].x < -50 || bullets[i].x >(float)SCREEN_WIDTH + 50) bullets[i].isActive = false;
            }
        }

        //変更あり：敵状態の算出
        if (enemy.hp > 0 && CanUpdate(enemy.x, enemy.y, enemy.width, enemy.height, enemy.scale)) {
            float pCenterX = player.x + (player.width * player.scale) / 2.0f;
            float pCenterY = player.y + (player.height * player.scale) / 2.0f;
            float eCenterX = enemy.x + (enemy.width * enemy.scale) / 2.0f;
            float eCenterY = enemy.y + (enemy.height * enemy.scale) / 2.0f;
            float distX = std::abs(pCenterX - eCenterX);
            float distY = std::abs(pCenterY - eCenterY);

            if (distX <= ENEMY_ATTACK_RANGE_X && distY <= ENEMY_ATTACK_RANGE_Y) enemy.state = ATTACK;
            else enemy.state = PATROL;

            if (enemy.state == PATROL) {
                if (enemy.direction == 1) {
                    enemy.vx = -ENEMY_WALK_SPEED;
                    if (enemy.x <= enemy.patrolLeft) enemy.direction = 0;
                }
                else {
                    enemy.vx = ENEMY_WALK_SPEED;
                    if (enemy.x >= enemy.patrolRight) enemy.direction = 1;
                }
            }
            else if (enemy.state == ATTACK) {
                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                if (enemy.attackTimer > 0) enemy.attackTimer -= 1.0f * ts;

                if (enemy.attackTimer <= 0) {
                    for (int i = 0; i < MAX_BULLETS; i++) {
                        if (!enemyBullets[i].isActive) {
                            enemyBullets[i].isActive = true;
                            enemyBullets[i].x = enemy.x + (enemy.direction == 0 ? (float)enemy.width * enemy.scale : -10.0f);
                            enemyBullets[i].y = enemy.y + (float)enemy.height * enemy.scale / 4.0f;
                            enemyBullets[i].vx = (enemy.direction == 0 ? BULLET_SPEED * 0.5f : -BULLET_SPEED * 0.5f);
                            break;
                        }
                    }
                    enemy.attackTimer = ENEMY_ATTACK_COOLDOWN;
                }
            }
            enemy.vy += GRAVITY * ts;
            enemy.x += enemy.vx * ts;
            enemy.y += enemy.vy * ts;
        }

        for (int i = 0; i < MAX_BULLETS; i++) {
            if (enemyBullets[i].isActive && CanUpdate(enemyBullets[i].x, enemyBullets[i].y, enemyBullets[i].width, enemyBullets[i].height, 1.0f)) {
                enemyBullets[i].x += enemyBullets[i].vx * ts;
                if (enemyBullets[i].x < -50 || enemyBullets[i].x >(float)SCREEN_WIDTH + 50) enemyBullets[i].isActive = false;
            }
        }
        //ちくわブロック状態関連：条件追加（CanUpdate()）
        if (CanUpdate(chikuwa.x, chikuwa.y, chikuwa.width, chikuwa.height, 1.0f)) {
            if (chikuwa.isPlayerOn && !chikuwa.isFalling) {
                chikuwa.rideTimer += 1.0f * ts;
                if (chikuwa.rideTimer >= chikuwa.fallDelay) chikuwa.isFalling = true;
            }
            else if (!chikuwa.isFalling) {
                chikuwa.rideTimer = 0.0f;
                chikuwa.y = chikuwa.originalY;
            }

            if (chikuwa.isFalling) {
                chikuwa.vy += GRAVITY * ts;
                chikuwa.y += chikuwa.vy * ts;
                if (chikuwa.y > SCREEN_HEIGHT + 100) {
                    chikuwa.rideTimer += 1.0f * ts;
                    if (chikuwa.rideTimer > 180.0f) {
                        chikuwa.isFalling = false;
                        chikuwa.y = chikuwa.originalY;
                        chikuwa.vy = 0;
                        chikuwa.rideTimer = 0.0f;
                    }
                }
            }
        }

        // 当たり判定・接地判定
        if (currentScene == PLAY) {
            //感圧板
            float pX2 = player.x + player.width * player.scale;
            float pY2 = player.y + player.height * player.scale;

            if (pX2 > plate.x && player.x < plate.x + plate.width && pY2 >= groundY - 2.0f && pY2 <= groundY + 2.0f) {
                plate.isPressed = (player.scale >= plate.requiredScale);//プレイヤーのサイズと関係ある
            }
            else {
                plate.isPressed = false;
            }

            if (pX2 > chikuwa.x && player.x < chikuwa.x + chikuwa.width && std::abs(pY2 - chikuwa.y) < 5.0f && player.vy >= 0) {
                chikuwa.isPlayerOn = true;
                player.y = chikuwa.y - player.height * player.scale;
                player.vy = 0;
                player.isJumping = false;
            }
            else {
                chikuwa.isPlayerOn = false;
            }

            // 敵Bの当たり判定（喰う処理）
            if (chomper.isActive) {
                // 対プレイヤー（触れると即ゲームオーバー）
                if (CheckCollision(player.x, player.y, player.width * player.scale, player.height * player.scale,
                    chomper.x, chomper.y, chomper.width, chomper.height)) {
                    player.hp = 0;
                }

                // 対敵（触れると敵を即死させ、自身も消える）
                if (enemy.hp > 0 && CheckCollision(enemy.x, enemy.y, enemy.width * enemy.scale, enemy.height * enemy.scale,
                    chomper.x, chomper.y, chomper.width, chomper.height)) {
                    enemy.hp = 0;
                    chomper.isActive = false; // お互いに消滅
                }
            }

            //弾のあたり判定
            //敵へ
            if (enemy.hp > 0) {
                for (int i = 0; i < MAX_BULLETS; i++) {
                    if (bullets[i].isActive && CheckCollision(bullets[i].x, bullets[i].y, bullets[i].width, bullets[i].height, enemy.x, enemy.y, enemy.width * enemy.scale, enemy.height * enemy.scale)) {
                        bullets[i].isActive = false;
                        enemy.hp--;
                    }
                }
            }
            //プレイヤーへ
            for (int i = 0; i < MAX_BULLETS; i++) {
                if (enemyBullets[i].isActive && CheckCollision(enemyBullets[i].x, enemyBullets[i].y, enemyBullets[i].width, enemyBullets[i].height, player.x, player.y, player.width * player.scale, player.height * player.scale)) {
                    enemyBullets[i].isActive = false;
                    player.hp--;
                }
            }
        }

        //Always on ground
        if (player.y + (float)player.height * player.scale > groundY) {
            player.y = groundY - (float)player.height * player.scale;
            player.vy = 0; player.isJumping = false;
        }
        if (enemy.y + (float)enemy.height * enemy.scale > groundY) {
            enemy.y = groundY - (float)enemy.height * enemy.scale;
            enemy.vy = 0;
        }

        // 描画処理 (gameScreen)
        for (int i = 0; i < SCREEN_WIDTH / 64 + 1; i++) DrawGraph(i * 64, (int)groundY, yukaHandle, TRUE);

        //pressure plateの描画
        if (plate.isPressed) DrawBox((int)plate.x, (int)plate.y + 5, (int)(plate.x + plate.width), (int)(plate.y + plate.height), GetColor(0, 238, 0), TRUE);
        else DrawBox((int)plate.x, (int)plate.y, (int)(plate.x + plate.width), (int)(plate.y + plate.height), GetColor(150, 150, 150), TRUE);
        DrawString((int)plate.x - 10, (int)plate.y - 15, "Mass>=1.5", GetColor(255, 255, 255));

        //ちくわブロックの描画
        float shakeX = 0.0f;
        //乗られていて、まだ落ちていない時はカタカタ揺らす
        if (chikuwa.rideTimer > 0 && !chikuwa.isFalling) shakeX = sinf(chikuwa.rideTimer * 1.0f) * 3.0f;
        
        DrawBox((int)(chikuwa.x + shakeX), (int)chikuwa.y, (int)(chikuwa.x + chikuwa.width + shakeX), (int)(chikuwa.y + chikuwa.height), GetColor(222, 184, 135), TRUE);
        DrawBox((int)(chikuwa.x + shakeX), (int)chikuwa.y, (int)(chikuwa.x + chikuwa.width + shakeX), (int)(chikuwa.y + chikuwa.height), GetColor(139, 69, 19), FALSE);

        // 敵Bの描画（.pngではない）
        if (chomper.isActive) {
            DrawBox((int)chomper.x, (int)chomper.y, (int)(chomper.x + chomper.width), (int)(chomper.y + chomper.height), GetColor(128, 0, 128), TRUE);
        }

        //プレイヤーの上にHPの描画
        if (player.hp > 0) {
            int cx = (int)(player.x + (player.width * player.scale) / 2.0f);
            int cy = (int)(player.y + (player.height * player.scale) / 2.0f);
            if (player.direction == 0) DrawRotaGraph(cx, cy, player.scale, player.angle, player.handle, TRUE);
            else DrawRotaGraph(cx, cy, player.scale, player.angle, player.handle, TRUE, TRUE);
            DrawFormatString((int)player.x, (int)player.y - 20, GetColor(0, 255, 0), "PLAYER HP:%d", player.hp);
        }

        //弾
        for (int i = 0; i < MAX_BULLETS; i++) if (bullets[i].isActive) DrawGraph((int)bullets[i].x, (int)bullets[i].y, bullets[i].handle, TRUE);

        //敵の上にHPの描画
        if (enemy.hp > 0) {
            int ex = (int)(enemy.x + (enemy.width * enemy.scale) / 2.0f);
            int ey = (int)(enemy.y + (enemy.height * enemy.scale) / 2.0f);
            if (enemy.direction == 0) DrawRotaGraph(ex, ey, enemy.scale, 0.0f, enemy.handle, TRUE);
            else DrawRotaGraph(ex, ey, enemy.scale, 0.0f, enemy.handle, TRUE, TRUE);
            DrawFormatString((int)enemy.x, (int)enemy.y - 20, GetColor(255, 0, 0), "ENEMY HP:%d", enemy.hp);
        }

        //敵の弾
        for (int i = 0; i < MAX_BULLETS; i++) if (enemyBullets[i].isActive) DrawGraph((int)enemyBullets[i].x, (int)enemyBullets[i].y, enemyBullets[i].handle, TRUE);

        //装置の描画
        SetDrawBlendMode(DX_BLENDMODE_ALPHA, 60);
        DrawCircle((int)tField.x, (int)tField.y, (int)tField.radius, GetColor(0, 255, 255), TRUE);
        SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
        DrawCircle((int)tField.x, (int)tField.y, (int)tField.radius, GetColor(0, 255, 255), FALSE);

        // 結果画面
        if (currentScene != PLAY) {
            SetDrawBlendMode(DX_BLENDMODE_ALPHA, 180);
            DrawBox(0, 0, SCREEN_WIDTH, SCREEN_HEIGHT, GetColor(0, 0, 0), TRUE);
            SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);

            int btnW = 160, btnH = 50;
            int btnX = SCREEN_WIDTH / 2 - btnW / 2;
            int btnY = SCREEN_HEIGHT / 2 + 30;

            bool isHover = (gx >= btnX && gx <= btnX + btnW && gy >= btnY && gy <= btnY + btnH);

            if (currentLeftClick && !lastLeftClick && isHover) {
                ResetGame();
                uiHandled = true;
            }

            SetFontSize(48);
            if (currentScene == RESULT_GAMEOVER) {
                DrawString(SCREEN_WIDTH / 2 - 120, SCREEN_HEIGHT / 2 - 50, "GAME OVER", GetColor(255, 50, 50));
            }
            else {
                DrawString(SCREEN_WIDTH / 2 - 100, SCREEN_HEIGHT / 2 - 50, "VICTORY!", GetColor(255, 255, 50));
            }
            SetFontSize(16);

            DrawBox(btnX, btnY, btnX + btnW, btnY + btnH, isHover ? GetColor(150, 150, 150) : GetColor(80, 80, 80), TRUE);
            DrawBox(btnX, btnY, btnX + btnW, btnY + btnH, GetColor(255, 255, 255), FALSE);
            DrawString(btnX + 55, btnY + 17, "RETRY", GetColor(255, 255, 255));
        }

        // 編集画面
        SetDrawScreen(DX_SCREEN_BACK);
        ClearDrawScreen();
        if (isEditMode) {
            DrawBox(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT, GetColor(30, 30, 30), TRUE);
            DrawBox(0, 0, 250, WINDOW_HEIGHT - 100, GetColor(45, 45, 45), TRUE);
            DrawBox(WINDOW_WIDTH - 250, 0, WINDOW_WIDTH, WINDOW_HEIGHT - 100, GetColor(45, 45, 45), TRUE);
            DrawBox(0, WINDOW_HEIGHT - 100, WINDOW_WIDTH, WINDOW_HEIGHT, GetColor(40, 40, 40), TRUE);
            DrawBox(monitorX - 2, monitorY - 2, monitorX + SCREEN_WIDTH + 2, monitorY + SCREEN_HEIGHT + 2, GetColor(100, 100, 100), FALSE);

            DrawGraph(monitorX, monitorY, gameScreen, TRUE);

            DrawBox(50, WINDOW_HEIGHT - 60, WINDOW_WIDTH - 50, WINDOW_HEIGHT - 40, GetColor(60, 60, 60), TRUE);
            float mxp = 50 + (player.x / (float)SCREEN_WIDTH) * (WINDOW_WIDTH - 100);
            DrawBox((int)mxp - 2, WINDOW_HEIGHT - 70, (int)mxp + 2, WINDOW_HEIGHT - 30, GetColor(255, 0, 0), TRUE);
            for (auto& cp : cuts) {
                int x1 = 50 + cp.timePos * (WINDOW_WIDTH - 100), x2 = 50 + cp.targetTimePos * (WINDOW_WIDTH - 100);
                SetDrawBlendMode(DX_BLENDMODE_ALPHA, 80); DrawBox(x1, WINDOW_HEIGHT - 60, x2, WINDOW_HEIGHT - 40, GetColor(200, 50, 50), TRUE); SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
                DrawLine(x1, WINDOW_HEIGHT - 60, x1, WINDOW_HEIGHT - 40, GetColor(255, 255, 0)); DrawLine(x2, WINDOW_HEIGHT - 60, x2, WINDOW_HEIGHT - 40, GetColor(255, 255, 0));
            }
            if (tempCutStart >= 0) { int px = 50 + tempCutStart * (WINDOW_WIDTH - 100); DrawLine(px, WINDOW_HEIGHT - 75, px, WINDOW_HEIGHT - 25, GetColor(0, 255, 255)); }

            DrawString(WINDOW_WIDTH - 240, 20, "[INSPECTOR]", GetColor(200, 200, 200));
            DrawFormatString(WINDOW_WIDTH - 240, 50, isInspScale ? GetColor(255, 255, 0) : GetColor(255, 255, 255), "Scale: %.2f", player.scale);
            DrawFormatString(WINDOW_WIDTH - 240, 70, isInspAngle ? GetColor(255, 255, 0) : GetColor(255, 255, 255), "Angle: %.2f", player.angle);
            DrawFormatString(WINDOW_WIDTH - 240, 90, isInspSpeed ? GetColor(255, 255, 0) : GetColor(255, 255, 255), "Speed: %.1f", player.speedScale);
            //ちくわブロック関連（正直この設定がなくてもいいんじゃないって気持ちがある）
            DrawFormatString(WINDOW_WIDTH - 240, 115, isInspChikuwa ? GetColor(255, 255, 0) : GetColor(255, 255, 255), "Chikuwa Delay: %.1f", chikuwa.fallDelay);

            DrawBox(WINDOW_WIDTH / 2 - 50, WINDOW_HEIGHT - 90, WINDOW_WIDTH / 2 + 50, WINDOW_HEIGHT - 70, GetColor(80, 80, 80), TRUE);
            DrawString(WINDOW_WIDTH / 2 - 25, WINDOW_HEIGHT - 85, isPaused ? "RESUME" : "PAUSE", GetColor(255, 255, 255));

            if (menu.isOpen) {
                DrawBox(menu.x, menu.y, menu.x + menu.width, menu.y + menu.height, GetColor(50, 50, 50), TRUE);
                DrawBox(menu.x, menu.y, menu.x + menu.width, menu.y + menu.height, GetColor(180, 180, 180), FALSE);
                DrawString(menu.x + 10, menu.y + 10, "Speed +0.5", GetColor(255, 255, 255));
                DrawString(menu.x + 10, menu.y + 35, "Speed -0.5", GetColor(255, 255, 255));
                DrawString(menu.x + 10, menu.y + 60, "Flip Player", GetColor(255, 255, 255));
                DrawString(menu.x + 10, menu.y + 85, "Reset All", GetColor(255, 255, 255));
            }
            DrawString(10, 10, "PROFESSIONAL EDIT MODE", GetColor(255, 255, 0));
        }
        else {
            DrawExtendGraph(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT, gameScreen, TRUE);
            if (isPaused) DrawString(WINDOW_WIDTH / 2 - 40, WINDOW_HEIGHT / 2, "PAUSED", GetColor(255, 255, 255));
        }
        lastShot = currentShot; lastLeftClick = currentLeftClick; lastRightClick = currentRightClick;
        ScreenFlip();
    }
    DxLib_End();
    return 0;
}