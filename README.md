# Sciencetopia 后端

## 概述
Sciencetopia的后端是一个基于.NET 7 Web API的服务，它与Neo4j图数据库进行交互，为前端应用提供数据支持和业务逻辑处理。

## 主要特性
- **用户认证**：支持用户注册、登录和注销。
- **知识图谱管理**：提供知识图谱节点的获取和搜索功能。
- **学习计划**：允许用户创建和保存个性化学习计划。
- **收藏功能**：用户可以收藏他们感兴趣的知识节点。
- **推荐系统**：基于用户活动提供个性化推荐。

## 技术栈
- **.NET 7 Web API**：用于构建RESTful API服务。
- **Neo4j**：图数据库，用于存储和查询复杂的知识图谱数据。
- **身份验证**：使用Cookie认证机制。
- **CORS**：跨源资源共享支持。

## 如何开始
1. **克隆仓库**：`git clone https://github.com/Sciencetopia-org/SciencetopiaWebApplication.git`
2. **安装依赖**：确保已安装.NET 7 SDK。
3. **配置数据库**：在`appsettings.json`中配置Neo4j数据库连接。
4. **运行应用**：使用`dotnet run`命令启动Web API服务。

## API文档
- API文档通过Swagger生成，可在开发模式下访问`/swagger`查看。
