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

// Enemy Constants
const float ENEMY_WALK_SPEED = 2.0f;
const float ENEMY_ATTACK_RANGE_X = 250.0f;
const float ENEMY_ATTACK_RANGE_Y = 100.0f;
const float ENEMY_ATTACK_COOLDOWN = 60.0f;

enum EnemyState {
    PATROL,
    ATTACK
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
};

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
};

struct CutPoint {
    float timePos;
    float targetTimePos;
};

struct Bullet {
    float x, y, vx;
    bool isActive;
    int handle;
};

struct ContextMenu {
    bool isOpen;
    int x, y, width, height;
};

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

    Player player = { 100.0f, 300.0f, 0.0f, 0.0f, playerHandle, 0, false, pw, ph, 1.0f, 0.0f, 1.0f };
    Enemy enemy = { 450.0f, 300.0f, 0.0f, 0.0f, enemyHandle, 1, ew, eh, 1.0f, PATROL, 300.0f, 600.0f, 0.0f };

    std::vector<CutPoint> cuts;
    std::vector<Bullet> bullets(MAX_BULLETS);
    std::vector<Bullet> enemyBullets(MAX_BULLETS);

    for (int i = 0; i < MAX_BULLETS; i++) {
        bullets[i].isActive = false; bullets[i].handle = bulletHandle;
        enemyBullets[i].isActive = false; enemyBullets[i].handle = enemyBulletHandle;
    }

    float groundY = 400.0f;
    bool isPaused = false, isEditMode = false, isFastForward = false, isStepFrame = false;
    bool isDragging = false, isScaling = false, isRotating = false;
    bool isInspScale = false, isInspAngle = false, isInspSpeed = false;
    float dragOffsetX = 0, dragOffsetY = 0, baseScale = 1.0f, baseAngle = 0.0f, baseSpeed = 1.0f;
    int lastMouseX = 0, lastMouseY = 0;
    float tempCutStart = -1.0f, globalTimeScale = 1.0f;
    ContextMenu menu = { false, 0, 0, 160, 120 };
    SetMouseDispFlag(TRUE);

    int monitorX = (WINDOW_WIDTH - SCREEN_WIDTH) / 2;
    int monitorY = (WINDOW_HEIGHT - SCREEN_HEIGHT) / 2 - 40;

    while (ProcessMessage() == 0 && CheckHitKey(KEY_INPUT_ESCAPE) == 0)
    {
        int mx, my;
        GetMousePoint(&mx, &my);
        float gx = (float)mx, gy = (float)my;
        if (isEditMode) { gx = (float)(mx - monitorX); gy = (float)(my - monitorY); }

        static bool lastMiddleClick = false;
        bool currentMiddleClick = (GetMouseInput() & MOUSE_INPUT_MIDDLE) != 0;
        if (currentMiddleClick && !lastMiddleClick) { isEditMode = !isEditMode; menu.isOpen = false; }
        lastMiddleClick = currentMiddleClick;

        static bool lastPauseKey = false;
        if (CheckHitKey(KEY_INPUT_B) && !lastPauseKey) isPaused = !isPaused;
        lastPauseKey = (CheckHitKey(KEY_INPUT_B) != 0);

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

        if (isEditMode) {
            if (currentRightClick && !lastRightClick) { menu.isOpen = true; menu.x = mx; menu.y = my; }
            if (currentLeftClick && !lastLeftClick && menu.isOpen) {
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
                        player.scale = 1.0f; player.angle = 0.0f; player.speedScale = 1.0f;
                        menu.isOpen = false;
                    }
                }
                else { menu.isOpen = false; }
            }

            if (currentLeftClick && !menu.isOpen) {
                if (!lastLeftClick && mx >= WINDOW_WIDTH / 2 - 50 && mx <= WINDOW_WIDTH / 2 + 50 && my >= WINDOW_HEIGHT - 90 && my <= WINDOW_HEIGHT - 70) isPaused = !isPaused;

                // Inspector Dragging (Scrubbing)
                if (!lastLeftClick && mx >= WINDOW_WIDTH - 240) {
                    if (my >= 45 && my <= 65) { isInspScale = true; lastMouseX = mx; baseScale = player.scale; }
                    else if (my >= 66 && my <= 85) { isInspAngle = true; lastMouseX = mx; baseAngle = player.angle; }
                    else if (my >= 86 && my <= 105) { isInspSpeed = true; lastMouseX = mx; baseSpeed = player.speedScale; }
                }

                if (!lastLeftClick && my >= WINDOW_HEIGHT - 60 && my <= WINDOW_HEIGHT - 20) {
                    if (CheckHitKey(KEY_INPUT_LCONTROL)) {
                        float ct = (float)(mx - 50) / (WINDOW_WIDTH - 100);
                        if (tempCutStart < 0) tempCutStart = ct;
                        else { cuts.push_back({ (ct > tempCutStart ? tempCutStart : ct), (ct > tempCutStart ? ct : tempCutStart) }); tempCutStart = -1.0f; }
                    }
                }
                if (!isDragging && !isScaling && !isRotating) {
                    if (gx >= player.x && gx <= player.x + player.width * player.scale && gy >= player.y && gy <= player.y + player.height * player.scale) {
                        if (CheckHitKey(KEY_INPUT_S)) { isScaling = true; lastMouseY = my; baseScale = player.scale; }
                        else if (CheckHitKey(KEY_INPUT_R)) { isRotating = true; lastMouseX = mx; baseAngle = player.angle; }
                        else { isDragging = true; dragOffsetX = gx - player.x; dragOffsetY = gy - player.y; }
                    }
                }
                if (isDragging) { player.x = gx - dragOffsetX; player.y = gy - dragOffsetY; player.vx = 0; player.vy = 0; }
                if (isScaling) { player.scale = baseScale + (float)(lastMouseY - my) * 0.01f; if (player.scale < 0.1f) player.scale = 0.1f; }
                if (isRotating) { player.angle = baseAngle + (float)(mx - lastMouseX) * 0.02f; }

                // Update Inspector scrubbing
                if (isInspScale) { player.scale = baseScale + (float)(mx - lastMouseX) * 0.01f; if (player.scale < 0.1f) player.scale = 0.1f; }
                if (isInspAngle) { player.angle = baseAngle + (float)(mx - lastMouseX) * 0.02f; }
                if (isInspSpeed) { player.speedScale = baseSpeed + (float)(mx - lastMouseX) * 0.05f; if (player.speedScale < 0) player.speedScale = 0; }
            }
            else {
                isDragging = isScaling = isRotating = false;
                isInspScale = isInspAngle = isInspSpeed = false;
            }
        }

        static bool lastShot = false;
        bool currentShot = (CheckHitKey(KEY_INPUT_RETURN) != 0) || (!isEditMode && currentLeftClick);
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
        lastShot = currentShot; lastLeftClick = currentLeftClick; lastRightClick = currentRightClick;

        if (!isEditMode || !isPaused) {
            bool isShift = (CheckHitKey(KEY_INPUT_LSHIFT) || CheckHitKey(KEY_INPUT_RSHIFT));
            float speed = isShift ? DASH_SPEED : WALK_SPEED;
            player.vx = 0;
            if (CheckHitKey(KEY_INPUT_A)) { player.vx = -speed; player.direction = 1; }
            if (CheckHitKey(KEY_INPUT_D)) { player.vx = speed; player.direction = 0; }
            if (CheckHitKey(KEY_INPUT_SPACE) && !player.isJumping) { player.vy = JUMP_POWER; player.isJumping = true; }
        }

        SetDrawScreen(gameScreen);
        ClearDrawScreen();
        if ((!isPaused && !isDragging && !isScaling && !isRotating) || isStepFrame) {
            float ts = isStepFrame ? 1.0f : finalTimeScale;
            player.vy += GRAVITY * ts; player.x += player.vx * ts; player.y += player.vy * ts;
            float tp = player.x / (float)SCREEN_WIDTH;
            for (auto& cp : cuts) {
                if (player.vx > 0 && tp >= cp.timePos && tp < cp.targetTimePos) player.x = cp.targetTimePos * (float)SCREEN_WIDTH;
                else if (player.vx < 0 && tp <= cp.targetTimePos && tp > cp.timePos) player.x = cp.timePos * (float)SCREEN_WIDTH;
            }
            for (int i = 0; i < MAX_BULLETS; i++) { if (bullets[i].isActive) { bullets[i].x += bullets[i].vx * ts; if (bullets[i].x < -50 || bullets[i].x >(float)SCREEN_WIDTH + 50) bullets[i].isActive = false; } }

            float distX = std::abs(player.x - enemy.x);
            float distY = std::abs(player.y - enemy.y);

            if (distX <= ENEMY_ATTACK_RANGE_X && distY <= ENEMY_ATTACK_RANGE_Y) {
                enemy.state = ATTACK;
            }
            else {
                enemy.state = PATROL;
            }

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
                enemy.vx = 0.0f;
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

            for (int i = 0; i < MAX_BULLETS; i++) { if (enemyBullets[i].isActive) { enemyBullets[i].x += enemyBullets[i].vx * ts; if (enemyBullets[i].x < -50 || enemyBullets[i].x >(float)SCREEN_WIDTH + 50) enemyBullets[i].isActive = false; } }
        }

        // Always ground collision
        if (player.y + (float)player.height * player.scale > groundY) {
            player.y = groundY - (float)player.height * player.scale;
            player.vy = 0; player.isJumping = false;
        }
        if (enemy.y + (float)enemy.height * enemy.scale > groundY) {
            enemy.y = groundY - (float)enemy.height * enemy.scale;
            enemy.vy = 0;
        }

        for (int i = 0; i < SCREEN_WIDTH / 64 + 1; i++) DrawGraph(i * 64, (int)groundY, yukaHandle, TRUE);
        int cx = (int)(player.x + (player.width * player.scale) / 2.0f);
        int cy = (int)(player.y + (player.height * player.scale) / 2.0f);
        if (player.direction == 0) DrawRotaGraph(cx, cy, player.scale, player.angle, player.handle, TRUE);
        else DrawRotaGraph(cx, cy, player.scale, player.angle, player.handle, TRUE, TRUE);
        for (int i = 0; i < MAX_BULLETS; i++) if (bullets[i].isActive) DrawGraph((int)bullets[i].x, (int)bullets[i].y, bullets[i].handle, TRUE);

        int ex = (int)(enemy.x + (enemy.width * enemy.scale) / 2.0f);
        int ey = (int)(enemy.y + (enemy.height * enemy.scale) / 2.0f);
        if (enemy.direction == 0) DrawRotaGraph(ex, ey, enemy.scale, 0.0f, enemy.handle, TRUE);
        else DrawRotaGraph(ex, ey, enemy.scale, 0.0f, enemy.handle, TRUE, TRUE);

        for (int i = 0; i < MAX_BULLETS; i++) if (enemyBullets[i].isActive) DrawGraph((int)enemyBullets[i].x, (int)enemyBullets[i].y, enemyBullets[i].handle, TRUE);

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
        ScreenFlip();
    }
    DxLib_End();
    return 0;
}