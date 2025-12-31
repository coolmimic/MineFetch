# MineFetch 部署指南

## 快速部署（推荐）

### 1. 上传代码到服务器

```bash
# 方式一：使用 Git
git clone https://github.com/your-repo/MineFetch.git
cd MineFetch

# 方式二：使用 scp
scp -r MineFetch/ user@your-server:/opt/
```

### 2. 运行部署脚本

```bash
cd /opt/MineFetch
sudo bash deploy.sh
```

首次运行会：
1. 自动安装 Docker
2. 创建 `.env` 配置文件
3. 提示你填写 Bot Token

### 3. 配置 Bot Token

```bash
nano /opt/minefetch/.env
```

填写：
```
BOT_TOKEN=8104075752:AAGUcKObdQSDRXNODbELB_kLkOhGudTM9_U
```

### 4. 重新运行部署

```bash
sudo bash deploy.sh
```

---

## 验证部署

```bash
# 检查服务状态
docker compose ps

# 查看日志
docker compose logs -f

# 测试 API
curl http://localhost:5000/swagger
```

---

## 配置域名和 HTTPS（可选）

### 1. 安装 Nginx

```bash
apt install nginx -y
```

### 2. 配置反向代理

```bash
cp nginx.conf /etc/nginx/sites-available/minefetch
ln -s /etc/nginx/sites-available/minefetch /etc/nginx/sites-enabled/
nginx -t && systemctl reload nginx
```

### 3. 配置 SSL（Let's Encrypt）

```bash
apt install certbot python3-certbot-nginx -y
certbot --nginx -d your-domain.com
```

### 4. 设置 Webhook

编辑 `/opt/minefetch/.env`：
```
WEBHOOK_URL=https://your-domain.com/api/webhook
```

重启服务：
```bash
cd /opt/minefetch
docker compose restart
```

---

## 本地采集端连接

在你的本地电脑上，修改 `appsettings.json`：

```json
{
  "Backend": {
    "BaseUrl": "http://your-server-ip:5000",
    "Enabled": true
  }
}
```

然后运行采集端：
```bash
dotnet run
```

---

## 常用命令

| 操作 | 命令 |
|------|------|
| 查看日志 | `docker compose logs -f` |
| 重启服务 | `docker compose restart` |
| 停止服务 | `docker compose down` |
| 更新部署 | `git pull && docker compose up -d --build` |
| 进入容器 | `docker exec -it minefetch-api sh` |
| 查看数据库 | `docker exec -it minefetch-db psql -U postgres minefetch` |
