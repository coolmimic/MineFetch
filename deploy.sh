#!/bin/bash
set -e

# ==========================================
#   MineFetch 自动部署脚本
#   说明：请在项目源码根目录运行 (如 ~/MineFetch)
# ==========================================

APP_DIR="/opt/minefetch"
SOURCE_DIR=$(pwd)

echo "=========================================="
echo "  biubiu~ MineFetch 部署启动 🚀"
echo "=========================================="

# 1. 检查运行位置
if [ ! -d "MineFetch.Api" ] || [ ! -f "docker-compose.yml" ]; then
    echo "❌ 错误：请在项目根目录下运行此脚本！"
    echo "   当前目录: $SOURCE_DIR"
    echo "   正确操作: cd ~/MineFetch && sudo bash deploy.sh"
    exit 1
fi

# 2. 检查 Root 权限
if [ "$EUID" -ne 0 ]; then
    echo "❌ 请使用 sudo 运行此脚本"
    exit 1
fi

# 3. 准备目录 & 同步代码
echo "📂 同步代码到 $APP_DIR ..."
mkdir -p $APP_DIR

# 复制核心项目文件 (强制覆盖，但避开 .env)
# 使用 cp -r 复制目录和文件
cp -r MineFetch.Api "$APP_DIR/"
cp -r MineFetch.Entities "$APP_DIR/"
cp -r MineFetch.Collector "$APP_DIR/"
cp docker-compose.yml "$APP_DIR/"
cp Dockerfile.api "$APP_DIR/"

if [ -f "nginx.conf" ]; then
    cp nginx.conf "$APP_DIR/"
fi

# 4. 检查环境配置
cd $APP_DIR

if [ ! -f .env ]; then
    echo "⚠️  创建默认配置文件..."
    cat > .env << 'EOF'
POSTGRES_PASSWORD=minefetch123
BOT_TOKEN=YOUR_BOT_TOKEN_HERE
WEBHOOK_URL=
EOF
    echo "❌ 请先编辑配置：nano $APP_DIR/.env"
    exit 1
fi

# 5. 检查 Docker 环境
if ! command -v docker &> /dev/null; then
    echo "📦 安装 Docker..."
    curl -fsSL https://get.docker.com | sh
fi

# 6. 启动服务
echo "� 正在构建并启动服务..."
# 尝试使用新版 docker compose 命令，失败则回退到 docker-compose
if docker compose version &> /dev/null; then
    docker compose up -d --build
else
    docker-compose up -d --build
fi

echo ""
echo "=========================================="
echo "  ✅ 部署成功！"
echo "=========================================="
echo "  API 地址: http://localhost:5000"
echo "  工作目录: $APP_DIR"
echo ""
