# NetworkMapViewerV2

**NetworkMapViewerV2** is an enterprise-focused C# WPF application designed for interactive network topology mapping, asynchronous device health monitoring, and legacy network map migration.

---

## Key Features

- 🗺️ **Interactive Topology Mapping:** Drag-and-drop workspace supporting network nodes, custom grouping containers, and annotations.
- ⚡ **Asynchronous Live Ping Engine:** Non-blocking background thread ICMP status tracking for network devices.
- 🗄️ **MS SQL Database Storage:** Persistent storage using a clean Repository Pattern.
- 📋 **Comprehensive Audit Logging:** Automatic transactional tracking (`INSERT`, `UPDATE`, `DELETE`) across all map entities.
- 🔍 **Search & Fast Navigation:** Real-time filtering across devices, IPs, and labels with hotkey support.
- 🧩 **MVVM Architecture:** Modular codebase leveraging Dependency Injection for scalability and maintainability.

---

## Prerequisites

- **Framework:** .NET 10+
- **IDE:** Visual Studio 2022 (with *.NET Desktop Development* workload)
- **Database:** Microsoft SQL Server (LocalDB, Express, or Standard/Enterprise)

---

## Quick Start

### 1. Clone the Repository
```bash
git clone https://github.com/KillerTonix/NetworkMapViewerV2.git
cd NetworkMapViewerV2
