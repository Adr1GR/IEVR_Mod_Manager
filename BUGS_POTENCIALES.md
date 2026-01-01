# Bugs Potenciales Encontrados en IEVR Mod Manager

## 🔴 Críticos - ✅ SOLUCIONADOS

### 1. **HttpClient no se dispone - Fuga de recursos** ✅ SOLUCIONADO
**Ubicación:** `MainWindow.xaml.cs:34`, `AppUpdateManager.cs:19`, `GameBananaBrowserWindow.xaml.cs:38`

**Problema:** Se crean instancias estáticas de `HttpClient` que nunca se disponen. Esto puede causar agotamiento de sockets (socket exhaustion) en aplicaciones de larga duración.

**Solución implementada:** 
- Se agregaron métodos estáticos `DisposeHttpClient()` en `MainWindow`, `AppUpdateManager` y `GameBananaBrowserWindow`
- Se llama a estos métodos desde `App.xaml.cs` en el método `OnExit()` para disponer correctamente de los recursos cuando la aplicación se cierra

---

### 2. **Process no se dispone en ViolaIntegration** ✅ SOLUCIONADO
**Ubicación:** `Managers/ViolaIntegration.cs:98, 163`

**Problema:** El objeto `Process` se crea pero nunca se dispone explícitamente. Aunque se establece a `null`, el proceso puede seguir consumiendo recursos.

**Solución implementada:**
- `ViolaIntegration` ahora implementa `IDisposable`
- Se agregó el método `DisposeProcess()` que correctamente dispone del proceso
- Se llama `Dispose()` en todos los lugares donde se limpia el proceso (`HandleProcessCompletion`, `HandleProcessError`, `Stop`)
- Se implementó el patrón Dispose estándar con `Dispose(bool disposing)`

---

### 3. **Race condition con Thread.Sleep en ConfigManager** ✅ SOLUCIONADO
**Ubicación:** `Managers/ConfigManager.cs:383`

**Problema:** Uso de `Thread.Sleep(50)` sugiere un intento de solucionar una condición de carrera, lo cual es una mala práctica.

**Solución implementada:**
- Se eliminó `Thread.Sleep(50)`
- Se implementó lógica de retry con `Task.Delay()` para verificar que el archivo se escribió correctamente
- Se agregó verificación del valor guardado para asegurar que la escritura fue exitosa
- Se mantiene compatibilidad hacia atrás con un método síncrono que llama a la versión async

---

## 🟡 Importantes - ✅ SOLUCIONADOS

### 4. **Null-forgiving operator puede ocultar NullReferenceException** ✅ SOLUCIONADO
**Ubicación:** `Managers/ProfileManager.cs:51, 57`

**Problema:** Uso de `!` (null-forgiving operator) puede ocultar errores reales de null.

**Solución implementada:**
- Se agregó verificación explícita de null antes de usar el operador `!`
- Se agregó un filtro adicional `.Where(profile => profile != null)` para asegurar que no se procesen perfiles null
- Se eliminó el uso innecesario del null-forgiving operator en la lista final

---

### 5. **Acceso a _config sin sincronización en operaciones async** ✅ SOLUCIONADO
**Ubicación:** `MainWindow.xaml.cs` (múltiples lugares)

**Problema:** `_config` se accede desde múltiples operaciones async sin sincronización adecuada, lo que puede causar condiciones de carrera.

**Solución implementada:**
- Se agregó un objeto `_configLock` para sincronización
- Se crearon métodos helper thread-safe `GetConfig()` y `SetConfig()` que usan `lock`
- Se actualizaron los accesos críticos a `_config` para usar estos métodos thread-safe
- Se protegieron especialmente los accesos desde callbacks async y métodos que pueden ejecutarse desde otros hilos

---

### 6. **Inicialización de _config con null! puede fallar** ✅ SOLUCIONADO
**Ubicación:** `MainWindow.xaml.cs:35`

**Problema:** `_config` se inicializa con `null!` pero si `LoadConfig()` falla, puede causar `NullReferenceException`.

**Solución implementada:**
- Se eliminó el uso de `null!` y se cambió a `AppConfig` sin inicialización forzada
- Se agregó manejo de excepciones en `LoadConfig()` que usa `AppConfig.Default()` si falla la carga
- El método `GetConfig()` siempre retorna un valor válido (usa `Default()` si es null)
- Se asegura que `_config` nunca sea null durante el uso de la aplicación

---

### 7. **Process.Start no se dispone en PlayButton_Click** ✅ SOLUCIONADO
**Ubicación:** `MainWindow.xaml.cs:1486`

**Problema:** El proceso iniciado para el juego no se dispone, aunque puede ser intencional si el proceso debe continuar ejecutándose.

**Solución implementada:**
- Se guarda la referencia del proceso en una variable local
- Se verifica que el proceso se inició correctamente antes de continuar
- Se establece `EnableRaisingEvents = false` para no rastrear el proceso (ya que debe continuar ejecutándose independientemente)
- El proceso no se dispone intencionalmente ya que debe continuar ejecutándose después de cerrar la aplicación

---

## 🟢 Menores

### 8. **Thread.Sleep en Logger puede ser ineficiente**
**Ubicación:** `Helpers/Logger.cs:277`

**Problema:** Uso de `Thread.Sleep(100)` en un loop puede ser reemplazado con mecanismos más eficientes.

```csharp
Thread.Sleep(100); // Small delay to batch writes
```

**Solución:** Usar `Task.Delay()` o `ManualResetEvent`/`SemaphoreSlim` para mejor rendimiento.

---

### 9. **Falta validación de null en WaitForProcessCompletionAsync**
**Ubicación:** `Managers/ViolaIntegration.cs:155`

**Problema:** Uso de `!` en `_currentProcess!` sin verificación previa.

```csharp
await _currentProcess!.WaitForExitAsync(); // ⚠️ Puede ser null
```

**Solución:** Verificar null antes de usar.

---

### 10. **Manejo de excepciones genérico puede ocultar errores**
**Ubicación:** Múltiples lugares con `catch (Exception ex)`

**Problema:** Capturar todas las excepciones puede ocultar errores específicos que deberían manejarse de manera diferente.

**Solución:** Capturar excepciones específicas cuando sea posible.

---

### 11. **Posible condición de carrera en progressWindow**
**Ubicación:** `MainWindow.xaml.cs:1423-1427`

**Problema:** `progressWindow` se establece a `null` dentro de `Dispatcher.Invoke`, pero puede ser accedido desde otros hilos.

```csharp
progressWindow.AllowClose();
progressWindow = null; // ⚠️ Puede ser accedido desde otro hilo
```

**Solución:** Usar sincronización adecuada o variables locales.

---

### 12. **Falta validación de Directory.Exists antes de operaciones**
**Ubicación:** Múltiples lugares

**Problema:** Algunas operaciones asumen que los directorios existen sin verificar primero.

**Solución:** Agregar validaciones antes de operaciones críticas.

---

## 📋 Recomendaciones Generales

1. **Implementar IDisposable** para clases que manejan recursos no administrados
2. **Usar IHttpClientFactory** en lugar de HttpClient estático
3. **Agregar sincronización** para acceso concurrente a `_config`
4. **Reemplazar Thread.Sleep** con mecanismos async apropiados
5. **Validar null** antes de usar null-forgiving operator
6. **Implementar logging** más detallado para debugging
7. **Agregar unit tests** para casos edge y condiciones de carrera

---

## 🔍 Archivos que Requieren Revisión

- `MainWindow.xaml.cs` - Múltiples problemas de sincronización y recursos
- `Managers/ViolaIntegration.cs` - Process no se dispone
- `Managers/ConfigManager.cs` - Thread.Sleep sugiere race condition
- `Managers/ProfileManager.cs` - Null-forgiving operators
- `Helpers/Logger.cs` - Thread.Sleep en loop
- `Managers/AppUpdateManager.cs` - HttpClient estático
- `Windows/GameBananaBrowserWindow.xaml.cs` - HttpClient estático

