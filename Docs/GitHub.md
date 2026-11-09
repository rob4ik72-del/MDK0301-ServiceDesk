# Работа с Git и GitHub

Проект: https://github.com/rob4ik72-del/MDK0301-ServiceDesk
Для сдачи передайте эту ссылку и отчёты из Reports.

## Получить и проверить опубликованный проект

1. Установите Git и .NET 8 SDK.
2. В PowerShell выполните:

```powershell
git clone https://github.com/rob4ik72-del/MDK0301-ServiceDesk.git
cd MDK0301-ServiceDesk
.\Verify.cmd
```

3. На GitHub откройте вкладку Actions и выберите последний запуск CSharp ServiceDesk.
4. В результатах Windows job откройте журнал выполнения Verification. Артефакт
   ServiceDesk-verification содержит журналы и стендовый протокол SAT.

## Отправить свои исправления

```powershell
git switch -c fix/report
git status
git add Reports README.md
git diff --cached --stat
git commit -m "Уточнить материалы отчётов"
git push -u origin fix/report
```

Откройте pull request из fix/report в main, дождитесь проверки и выполните Merge.
Если правите исходники, добавьте изменённые файлы из Code. Рабочие базы, токены,
закрытые ключи и локальные результаты не публикуйте.

## Опубликовать распакованную копию в новом репозитории

Создайте пустой репозиторий на GitHub без README. Откройте PowerShell в папке,
где лежит ServiceDesk.sln, и выполните:

```powershell
git init -b main
git add .
git status --short
git commit -m "Добавить портфолио МДК 03.01"
git remote add origin URL_ВАШЕГО_НОВОГО_РЕПОЗИТОРИЯ
git push -u origin main
```

Замените URL на HTTPS-адрес, который GitHub показал для вашего репозитория.
Эта последовательность предназначена для нового репозитория; существующий
проект сначала клонируйте. В своих отчётах указывайте фактический адрес публикации.
