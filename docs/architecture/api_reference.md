# Mapa General de la API y Subdominios Backend

A medida que el proyecto `VendingManager` ha ido creciendo, documentar los límites transaccionales es crucial para agilizar el pensamiento perimetral. Este documento cataloga todos los controladores de `src/VendingManager/Controllers/` definiendo qué resuelve su lógica en el ecosistema.

## 1. Módulo de Venta y Analítica (`VentasController.cs`)
Es el hub central. Conecta fuertemente con transacciones post-sincronización y reportería.

- **Importaciones:** `/subir-ventas-maquina`, `/subir-transbank`, `/sync-portal`
- **Dashboard & KPIs:** `/dashboard-stats`, `/ventas-diarias`
- **Analítica Pesada:** `/reporte-rango` (ingresos detallados), `/informe-financiero` (calculo de utilidades), `/analisis-productos` (Algoritmo clasificador Estrella/Cacho), `/stockout-analysis` (Cálculo de dinero perdido por quiebres).
- **Abastecimiento predictivo:** `/purchase-suggestion`, `/stock-critico`

## 2. Módulo Logístico Operativo (`OrdenCargaController.cs`)
Controla la salida física de mercancía de Bodega hacia la Máquina Expendora a manos del Reponedor.

- **Generación:** `POST /` (Genera una lista nueva basada en Template o Sugerencia).
- **Procesamiento:** `/finalizar` (Aplica los montos físicos al stock real del servidor).
- **Visibilidad:** `/historial`, `/exportar-consolidado`, `/exportar-sugerencia`.

## 3. Módulo Cajas & Finanzas Manuales (`CajaController.cs`)
Rastrea flujos de dinero físico y transacciones por fuera del ecosistema "Máquina/Transbank". Ej. gastos del personal, retiro de la gaveta de monedas.

- **Resúmenes Base:** `/resumen`, `/movimientos`
- **Registro:** `/registrar` (Ingresa gasto o ingreso libre), `/upload` (Boletas / vouchers).
- **Proyecciones:** `/valorizacion` (Da un costo monetario al SKU en bodega actual vs la calle).

## 4. Módulo Suministro y RRHH (`ComprasController.cs`)
Registra transacciones monetarias que resulten en adquisición de Stock real para Bodega y manejo de deudas a Proveedores.

- **Logística:** Creación `[POST]`, Actualización `[PUT]`.
- **Finanzas Puras:** `/pagar` (Convierte la deuda al proveedor en un `MovimientoCaja` liquidado).

## 5. Módulo Inventario & Catálogo Core (`InventarioController.cs` & `ProductosController.cs`)

- **`InventarioController`**: Domina la estandarización híbrida masiva (`/subir-catalogo`).
- **`ProductosController`**: Orientado al CRUD estricto del master de productos. (`/importar-catalogo`, `/exportar-catalogo`, `/ajustar-stock` directo sin órdenes de compra).

## 6. Módulo Activos Híbridos (`MaquinasController.cs`)
Abstrae el "Hardware virtual" (la estantería de ventas).

- **Gestión Atributos:** Típico CRUD general.
- **Topología (Slots):** `/{id}/slots`, `/{id}/batch-actions` (Para linkear SKUs a espirales/motores en la máquina real).

## 7. Módulos Utilitarios / Bases Integradas

- **`TemplateRecargaController.cs`**: Subdominio para precargar modelos repetitivos de rutas de reabastecimiento y re-utilizarlos.
- **`InformesController.cs`**: File System abstraído a SQL. Repositorio de todos los excels/vouchers alguna vez subidos.
- **`AuditoriaController.cs`**: Log transaccional puro.
- **`AccountController.cs` / `UsersController.cs`**: Capa delgada de Autorización/Autenticación JWT.

---

> [!TIP]
> **Para Crear Nuevas Funcionalidades:** 
> 1. Si tu nueva funcionalidad cruza dos mundos (Ej: Evaluar qué reponedor rinde más ganancia), trata de no contaminar `VentasController`. Piensa en un nuevo controlador, ej: `AuditoriaReponedoresController`.
> 2. Si las lógicas exceden a 3 tablas unidas, empuja el LINQ a un sub-servicio (como `SalesAnalyticsService`) en lugar de amontonarlo en el Controller.
