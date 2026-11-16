// Extra locales: DSH ships zh, en, ja, es, fr, de, ru and pt. The launcher keeps
// zh/en inline in Lang.cs; the other six live here and fall back to English for
// any key that is not translated yet.
using System.Collections.Generic;

namespace DshDesktop
{
    internal static partial class StringsExtra
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Tables =
            new Dictionary<string, Dictionary<string, string>>();

        private static Dictionary<string, string> Table(string code)
        {
            Dictionary<string, string> table;
            if (Tables.TryGetValue(code, out table)) return table;
            return null;
        }

        // Returns false when the key has no translation for this locale, so the
        // caller can fall back to English instead of showing a missing string.
        public static bool Lookup(string code, string key, out string text)
        {
            text = null;
            Dictionary<string, string> table = Table(code);
            if (table == null) return false;
            return table.TryGetValue(key, out text);
        }

        static StringsExtra()
        {
            Tables["ja"] = new Dictionary<string, string>
            {
                { "menu.settings", "設定…" },
                { "menu.checkUpdate", "dsh の更新を確認…" },
                { "menu.openInBrowser", "ブラウザで開く" },
                { "menu.copyUrl", "アドレスをコピー" },
                { "menu.devtools", "開発者ツール (F12)" },
                { "menu.openWorkspace", "ワークスペースを開く" },
                { "menu.openData", "データフォルダーを開く" },
                { "menu.openIni", "設定ファイルを開く" },
                { "menu.about", "バージョン情報" },
                { "settings.title", "設定" },
                { "settings.section.startup", "起動" },
                { "settings.section.look", "外観" },
                { "settings.section.folders", "フォルダー" },
                { "settings.section.advanced", "詳細" },
                { "settings.cancel", "キャンセル" },
                { "settings.save", "保存" },
                { "settings.saveRestart", "保存して再起動" },
                { "settings.openIni", "設定ファイルを開く" },
                { "settings.server", "内蔵 dsh web サービスを起動" },
                { "settings.port", "ポート" },
                { "settings.zoom", "初期ズーム" },
                { "about.title", "DSH Desktop について" },
                { "update.title", "dsh の更新" },
                { "update.now", "今すぐ更新" },
                { "update.close", "閉じる" },
                { "settings.applyAsk", "設定を保存しました。今すぐ再起動しますか？" },
                { "settings.applyNow", "再起動" },
                { "settings.applyLater", "後で" },
            };

            Tables["es"] = new Dictionary<string, string>
            {
                { "menu.settings", "Ajustes…" },
                { "menu.checkUpdate", "Buscar actualizaciones de dsh…" },
                { "menu.openInBrowser", "Abrir en el navegador" },
                { "menu.copyUrl", "Copiar dirección" },
                { "menu.devtools", "Herramientas de desarrollo (F12)" },
                { "menu.openWorkspace", "Abrir el espacio de trabajo" },
                { "menu.openData", "Abrir la carpeta de datos" },
                { "menu.openIni", "Abrir el archivo de configuración" },
                { "menu.about", "Acerca de" },
                { "settings.title", "Ajustes" },
                { "settings.section.startup", "Inicio" },
                { "settings.section.look", "Aspecto" },
                { "settings.section.folders", "Carpetas" },
                { "settings.section.advanced", "Avanzado" },
                { "settings.cancel", "Cancelar" },
                { "settings.save", "Guardar" },
                { "settings.saveRestart", "Guardar y reiniciar" },
                { "settings.openIni", "Abrir la configuración" },
                { "settings.server", "Iniciar el servicio dsh web integrado" },
                { "settings.port", "Puerto" },
                { "settings.zoom", "Zoom inicial" },
                { "about.title", "Acerca de DSH Desktop" },
                { "update.title", "Actualizar dsh" },
                { "update.now", "Actualizar ahora" },
                { "update.close", "Cerrar" },
                { "settings.applyAsk", "Ajustes guardados. ¿Reiniciar ahora para aplicarlos?" },
                { "settings.applyNow", "Reiniciar" },
                { "settings.applyLater", "Más tarde" },
            };

            Tables["fr"] = new Dictionary<string, string>
            {
                { "menu.settings", "Paramètres…" },
                { "menu.checkUpdate", "Rechercher des mises à jour de dsh…" },
                { "menu.openInBrowser", "Ouvrir dans le navigateur" },
                { "menu.copyUrl", "Copier l’adresse" },
                { "menu.devtools", "Outils de développement (F12)" },
                { "menu.openWorkspace", "Ouvrir l’espace de travail" },
                { "menu.openData", "Ouvrir le dossier de données" },
                { "menu.openIni", "Ouvrir le fichier de configuration" },
                { "menu.about", "À propos" },
                { "settings.title", "Paramètres" },
                { "settings.section.startup", "Démarrage" },
                { "settings.section.look", "Apparence" },
                { "settings.section.folders", "Dossiers" },
                { "settings.section.advanced", "Avancé" },
                { "settings.cancel", "Annuler" },
                { "settings.save", "Enregistrer" },
                { "settings.saveRestart", "Enregistrer et redémarrer" },
                { "settings.openIni", "Ouvrir la configuration" },
                { "settings.server", "Démarrer le service dsh web intégré" },
                { "settings.port", "Port" },
                { "settings.zoom", "Zoom initial" },
                { "about.title", "À propos de DSH Desktop" },
                { "update.title", "Mise à jour de dsh" },
                { "update.now", "Mettre à jour" },
                { "update.close", "Fermer" },
                { "settings.applyAsk", "Paramètres enregistrés. Redémarrer maintenant pour les appliquer ?" },
                { "settings.applyNow", "Redémarrer" },
                { "settings.applyLater", "Plus tard" },
            };

            Tables["de"] = new Dictionary<string, string>
            {
                { "menu.settings", "Einstellungen…" },
                { "menu.checkUpdate", "Nach dsh-Updates suchen…" },
                { "menu.openInBrowser", "Im Browser öffnen" },
                { "menu.copyUrl", "Adresse kopieren" },
                { "menu.devtools", "Entwicklertools (F12)" },
                { "menu.openWorkspace", "Arbeitsbereich öffnen" },
                { "menu.openData", "Datenordner öffnen" },
                { "menu.openIni", "Konfigurationsdatei öffnen" },
                { "menu.about", "Info" },
                { "settings.title", "Einstellungen" },
                { "settings.section.startup", "Start" },
                { "settings.section.look", "Darstellung" },
                { "settings.section.folders", "Ordner" },
                { "settings.section.advanced", "Erweitert" },
                { "settings.cancel", "Abbrechen" },
                { "settings.save", "Speichern" },
                { "settings.saveRestart", "Speichern und neu starten" },
                { "settings.openIni", "Konfiguration öffnen" },
                { "settings.server", "Eingebundenen dsh-web-Dienst starten" },
                { "settings.port", "Port" },
                { "settings.zoom", "Anfangszoom" },
                { "about.title", "Über DSH Desktop" },
                { "update.title", "dsh aktualisieren" },
                { "update.now", "Jetzt aktualisieren" },
                { "update.close", "Schließen" },
                { "settings.applyAsk", "Einstellungen gespeichert. Jetzt neu starten?" },
                { "settings.applyNow", "Neu starten" },
                { "settings.applyLater", "Später" },
            };

            Tables["ru"] = new Dictionary<string, string>
            {
                { "menu.settings", "Настройки…" },
                { "menu.checkUpdate", "Проверить обновления dsh…" },
                { "menu.openInBrowser", "Открыть в браузере" },
                { "menu.copyUrl", "Скопировать адрес" },
                { "menu.devtools", "Инструменты разработчика (F12)" },
                { "menu.openWorkspace", "Открыть рабочую папку" },
                { "menu.openData", "Открыть папку данных" },
                { "menu.openIni", "Открыть файл конфигурации" },
                { "menu.about", "О программе" },
                { "settings.title", "Настройки" },
                { "settings.section.startup", "Запуск" },
                { "settings.section.look", "Внешний вид" },
                { "settings.section.folders", "Папки" },
                { "settings.section.advanced", "Дополнительно" },
                { "settings.cancel", "Отмена" },
                { "settings.save", "Сохранить" },
                { "settings.saveRestart", "Сохранить и перезапустить" },
                { "settings.openIni", "Открыть конфигурацию" },
                { "settings.server", "Запускать встроенный dsh web" },
                { "settings.port", "Порт" },
                { "settings.zoom", "Начальный масштаб" },
                { "about.title", "О DSH Desktop" },
                { "update.title", "Обновление dsh" },
                { "update.now", "Обновить" },
                { "update.close", "Закрыть" },
                { "settings.applyAsk", "Настройки сохранены. Перезапустить сейчас?" },
                { "settings.applyNow", "Перезапустить" },
                { "settings.applyLater", "Позже" },
            };

            Tables["pt"] = new Dictionary<string, string>
            {
                { "menu.settings", "Configurações…" },
                { "menu.checkUpdate", "Procurar atualizações do dsh…" },
                { "menu.openInBrowser", "Abrir no navegador" },
                { "menu.copyUrl", "Copiar endereço" },
                { "menu.devtools", "Ferramentas de desenvolvedor (F12)" },
                { "menu.openWorkspace", "Abrir a área de trabalho" },
                { "menu.openData", "Abrir a pasta de dados" },
                { "menu.openIni", "Abrir o arquivo de configuração" },
                { "menu.about", "Sobre" },
                { "settings.title", "Configurações" },
                { "settings.section.startup", "Inicialização" },
                { "settings.section.look", "Aparência" },
                { "settings.section.folders", "Pastas" },
                { "settings.section.advanced", "Avançado" },
                { "settings.cancel", "Cancelar" },
                { "settings.save", "Salvar" },
                { "settings.saveRestart", "Salvar e reiniciar" },
                { "settings.openIni", "Abrir a configuração" },
                { "settings.server", "Iniciar o serviço dsh web integrado" },
                { "settings.port", "Porta" },
                { "settings.zoom", "Zoom inicial" },
                { "about.title", "Sobre o DSH Desktop" },
                { "update.title", "Atualizar o dsh" },
                { "update.now", "Atualizar agora" },
                { "update.close", "Fechar" },
                { "settings.applyAsk", "Configurações salvas. Reiniciar agora para aplicar?" },
                { "settings.applyNow", "Reiniciar" },
                { "settings.applyLater", "Depois" },
            };
        }
    }
}
