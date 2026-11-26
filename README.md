## 概述
此项目基于Unity2020.3.49f1c1开发，集成了UGF框架，luban表，YooAsset资源管理，HybridCLR代码热更的URP游戏框架，目的是整合一些我认为能为开发带来便利的工具，方便后续做其他游戏时不用再重新搭一遍。

## 运行流程
进入init场景，通过GameBoot启动游戏，可以通过GameBoot脚本配置启动包和启动场景，启动游戏后先进入热更界面，可通过Tools->HotUpdate->PublishWindow进行热更资源更新，热更完成后进入指定的Launch Scene,读取luban表Hero.xlsx并输出第一行记录。
## Ex
此项目目前不包含联机框架

## 引用
各个工具的使用方法详见以下链接：

[Game Framework | 基于 Unity 引擎的开源游戏框架](http://gameframework.cn/)

[使用指南 | HybridCLR](https://www.hybridclr.cn/docs/guide)

[Hello from YooAsset | YooAsset](https://www.yooasset.com/)

[什么是 Luban | Luban](https://www.datable.cn/docs/intro)
