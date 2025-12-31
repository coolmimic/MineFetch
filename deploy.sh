#!/bin/bash
set -e

# ==========================================
#   MineFetch 一键部署脚本
#   适用于 Ubuntu 22.04+
# ==========================================

echo "=========================================="
echo "  MineFetch 一键部署脚本"
echo "=========================================="
echo ""

# 检查是否为 root
if [ "$EUID" -ne 0 ]; then
    echo "请使用 sudo 运行此脚本"
    exit 1
fi

# 生成目录
APP_DIR="/opt/minefetch"
mkdir -p $APP_DIR
cd $APP_DIR

# 检查并安装 Docker
if ! command -v docker &> /dev/null; then
    echo "📦 安装 Docker..."
    curl -fsSL https://get.docker.com | sh
    systemctl enable docker
    systemctl start docker
    echo "✅ Docker 安装完成"
else
    echo "✅ Docker 已安装"
fi

# 检查并安装 Docker Compose
if ! command -v docker-compose &> /dev/null && ! docker compose version &> /dev/null; then
    echo "📦 安装 Docker Compose..."
    apt-get update && apt-get install -y docker-compose-plugin
    echo "✅ Docker Compose 安装完成"
else
    echo "✅ Docker Compose 已安装"
fi

# 检查 .env 文件
if [ ! -f .env ]; then
    cat > .env << 'EOF'
# MineFetch 配置文件
# 请修改以下配置

# PostgreSQL 密码
POSTGRES_PASSWORD=minefetch123

# Telegram Bot Token（必填）
BOT_TOKEN=YOUR_BOT_TOKEN_HERE

# Webhook URL（可选，留空使用轮询模式）
# 格式: https://your-domain.com/api/webhook
WEBHOOK_URL=
EOF
    echo ""
    echo "⚠️  已创建 .env 配置文件，请编辑后重新运行："
    echo "    nano $APP_DIR/.env"
    echo ""
    exit 1
fi

# 检查 BOT_TOKEN 是否配置
source .env
if [ "$BOT_TOKEN" = "YOUR_BOT_TOKEN_HERE" ] || [ -z "$BOT_TOKEN" ]; then
    echo "❌ 请先在 .env 中配置 BOT_TOKEN"
    echo "   nano $APP_DIR/.env"
    exit 1
fi

echo ""
echo "📥 开始部署..."

# 使用 docker compose 或 docker-compose
if docker compose version &> /dev/null; then
    COMPOSE_CMD="docker compose"
else
    COMPOSE_CMD="docker-compose"
fi

# 构建并启动
$COMPOSE_CMD up -d --build

echo ""
echo "=========================================="
echo "  ✅ 部署完成！"
echo "=========================================="
echo ""
echo "  API 地址: http://$(hostname -I | awk '{print $1}'):5000"
echo "  Swagger:  http://$(hostname -I | awk '{print $1}'):5000/swagger"
echo ""
echo "  管理命令:"
echo "    查看日志: cd $APP_DIR && $COMPOSE_CMD logs -f"
echo "    停止服务: cd $APP_DIR && $COMPOSE_CMD down"
echo "    重启服务: cd $APP_DIR && $COMPOSE_CMD restart"
echo ""

# 显示状态
$COMPOSE_CMD ps
