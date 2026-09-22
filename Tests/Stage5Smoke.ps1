# Run the API against a disposable, uniquely named MongoDB database before this script.
# Example: set MongoDB__DatabaseName and Logging__EventLog__LogLevel__Default=None
# in the API process, then pass its seeded Backoffice password via -AdminPassword.
param(
    [string]$BaseUrl = 'http://localhost:5151',
    [string]$AdminEmail = 'admin@smartsolar.com',
    [Parameter(Mandatory = $true)][string]$AdminPassword
)

$ErrorActionPreference = 'Stop'
$seed = Get-Date -Format 'yyMMddHHmmss'
$nicA = $seed
$nicB = ([long]$seed + 1).ToString('000000000000')
$password = 'Stage5Prosumer@123'

function Invoke-Api {
    param([string]$Method, [string]$Path, [object]$Body = $null, [string]$Token = $null)

    $arguments = @{ Uri = "$BaseUrl$Path"; Method = $Method; UseBasicParsing = $true }
    if ($Token) { $arguments.Headers = @{ Authorization = "Bearer $Token" } }
    if ($null -ne $Body) {
        $arguments.ContentType = 'application/json'
        $arguments.Body = $Body | ConvertTo-Json -Depth 10 -Compress
    }

    try {
        $response = Invoke-WebRequest @arguments
        $status = [int]$response.StatusCode
        $raw = $response.Content
    }
    catch {
        $response = $_.Exception.Response
        if ($null -eq $response) { throw }
        $status = [int]$response.StatusCode
        $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
        try { $raw = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }

    $data = $null
    if ($raw) {
        try { $data = ConvertFrom-Json -InputObject $raw -ErrorAction Stop }
        catch { $data = $null }
    }

    return [pscustomobject]@{ Status = $status; Data = $data; Raw = $raw }
}

function Check-Status {
    param([string]$Name, [object]$Response, [int]$Expected)

    Write-Host "$Name`: expected=$Expected actual=$($Response.Status)"
    if ($Response.Status -ne $Expected) {
        throw "$Name failed. Response: $($Response.Raw)"
    }
}

function Check-Value {
    param([string]$Name, [object]$Actual, [object]$Expected)

    Write-Host "$Name`: expected=$Expected actual=$Actual"
    if ($Actual -ne $Expected) { throw "$Name failed." }
}

function New-Slot {
    param([double]$HoursAhead)

    $start = [DateTime]::UtcNow.AddHours($HoursAhead)
    $response = Invoke-Api 'POST' '/api/slots' @{
        stationId = $mainStation.id
        slotDate = $start.ToString('o')
        startTime = $start.ToString('o')
        endTime = $start.AddHours(1).ToString('o')
        capacityKw = 5
    } $adminToken
    Check-Status "create slot +$HoursAhead hours" $response 201
    return $response.Data
}

function New-OwnReservation {
    param([object]$Slot, [string]$Token)

    $response = Invoke-Api 'POST' '/api/reservations/my' @{
        stationId = $mainStation.id
        slotId = $Slot.id
    } $Token
    Check-Status 'prosumer booking' $response 201
    return $response.Data.reservation
}

$adminLogin = Invoke-Api 'POST' '/api/auth/login' @{ email = $AdminEmail; password = $AdminPassword }
Check-Status 'Backoffice login' $adminLogin 200
$adminToken = $adminLogin.Data.token
if (-not $adminToken) { throw 'Backoffice login response did not contain a token.' }

foreach ($person in @(
    @{ nic = $nicA; name = 'Stage Five A'; email = "stage5-a-$seed@example.com" },
    @{ nic = $nicB; name = 'Stage Five B'; email = "stage5-b-$seed@example.com" }
)) {
    $registration = Invoke-Api 'POST' '/api/prosumers/register' @{
        nic = $person.nic
        name = $person.name
        email = $person.email
        password = $password
        contactNumber = '0771234567'
        address = 'Stage 5 smoke-test address'
        panelCapacityKw = 8.5
    }
    Check-Status "register $($person.name)" $registration 201
    $approval = Invoke-Api 'PUT' "/api/prosumers/$($person.nic)/reactivate" $null $adminToken
    Check-Status "approve $($person.name)" $approval 204
}

$loginA = Invoke-Api 'POST' '/api/auth/login' @{ email = "stage5-a-$seed@example.com"; password = $password }
Check-Status 'Prosumer A login' $loginA 200
$tokenA = $loginA.Data.token
$loginB = Invoke-Api 'POST' '/api/auth/login' @{ email = "stage5-b-$seed@example.com"; password = $password }
Check-Status 'Prosumer B login' $loginB 200
$tokenB = $loginB.Data.token

$operatorEmail = "stage5-operator-$seed@example.com"
$operatorUser = Invoke-Api 'POST' '/api/users' @{
    name = 'Stage Five Operator'
    email = $operatorEmail
    password = $password
    role = 'GridOperator'
} $adminToken
Check-Status 'create GridOperator' $operatorUser 201
$operatorLogin = Invoke-Api 'POST' '/api/auth/login' @{ email = $operatorEmail; password = $password }
Check-Status 'GridOperator login' $operatorLogin 200
$operatorToken = $operatorLogin.Data.token

$mainResponse = Invoke-Api 'POST' '/api/stations' @{
    stationName = "Stage Five Main $seed"
    latitude = 6.9271
    longitude = 79.8612
    capacityKw = 100
    availableSlots = 10
    schedule = '00:00-23:59'
} $adminToken
Check-Status 'create main station' $mainResponse 201
$mainStation = $mainResponse.Data

$otherResponse = Invoke-Api 'POST' '/api/stations' @{
    stationName = "Stage Five Other $seed"
    latitude = 6.9272
    longitude = 79.8613
    capacityKw = 100
    availableSlots = 10
    schedule = '00:00-23:59'
} $adminToken
Check-Status 'create other station' $otherResponse 201
$otherStation = $otherResponse.Data

$operatorAssignment = Invoke-Api 'PUT' "/api/users/$($operatorUser.Data.id)" @{
    stationId = $mainStation.id
} $adminToken
Check-Status 'assign GridOperator to main station' $operatorAssignment 200

$completionSlot = New-Slot 2
$pendingSlot = New-Slot 4
$approvedSlot = New-Slot 6
$cancelledSlot = New-Slot 30
$bSlot = New-Slot 10

$completionBooking = New-OwnReservation $completionSlot $tokenA
$pendingBooking = New-OwnReservation $pendingSlot $tokenA
$approvedBooking = New-OwnReservation $approvedSlot $tokenA
$cancelledBooking = New-OwnReservation $cancelledSlot $tokenA
$bBooking = New-OwnReservation $bSlot $tokenB

$cancelled = Invoke-Api 'PUT' "/api/reservations/my/$($cancelledBooking.id)/cancel" @{} $tokenA
Check-Status 'cancel future booking' $cancelled 200
$approved = Invoke-Api 'PUT' "/api/reservations/$($approvedBooking.id)/approve" $null $adminToken
Check-Status 'approve future booking' $approved 200
$approvedForCompletion = Invoke-Api 'PUT' "/api/reservations/$($completionBooking.id)/approve" $null $adminToken
Check-Status 'approve completion booking' $approvedForCompletion 200

$pendingQr = Invoke-Api 'GET' "/api/reservations/my/$($pendingBooking.id)/qr" $null $tokenA
Check-Status 'Pending booking has no QR' $pendingQr 404
$qrResponse = Invoke-Api 'GET' "/api/reservations/my/$($completionBooking.id)/qr" $null $tokenA
Check-Status 'approved booking QR' $qrResponse 200
$qr = $qrResponse.Data.qrToken
if (-not $qr) { throw 'Approved booking QR was empty.' }

$wrongStation = Invoke-Api 'POST' '/api/reservations/scan-complete' @{
    qrToken = $qr
    stationId = $otherStation.id
} $operatorToken
Check-Status 'wrong station scan' $wrongStation 403
$stillApproved = Invoke-Api 'GET' "/api/reservations/my/$($completionBooking.id)" $null $tokenA
Check-Status 'reservation after wrong-station scan' $stillApproved 200
Check-Value 'wrong-station scan preserved status' $stillApproved.Data.status 'Approved'

$pendingScan = Invoke-Api 'POST' '/api/reservations/scan-complete' @{
    qrToken = $pendingBooking.id
    stationId = $mainStation.id
} $operatorToken
Check-Status 'Pending reservation cannot be scanned' $pendingScan 400

$prosumerScan = Invoke-Api 'POST' '/api/reservations/scan-complete' @{
    qrToken = $qr
    stationId = $mainStation.id
} $tokenA
Check-Status 'Prosumer scan forbidden' $prosumerScan 403
$backofficeScan = Invoke-Api 'POST' '/api/reservations/scan-complete' @{
    qrToken = $qr
    stationId = $mainStation.id
} $adminToken
Check-Status 'Backoffice scan forbidden' $backofficeScan 403

$completed = Invoke-Api 'POST' '/api/reservations/scan-complete' @{
    qrToken = $qr
    stationId = $mainStation.id
} $operatorToken
Check-Status 'valid operator scan and complete' $completed 200
Check-Value 'completed reservation status' $completed.Data.status 'Completed'
Check-Value 'completedBy is operator token email' $completed.Data.completedBy $operatorEmail
$slotAfter = Invoke-Api 'GET' "/api/slots/$($completionSlot.id)" $null $adminToken
Check-Status 'completed slot lookup' $slotAfter 200
Check-Value 'completed slot is freed' $slotAfter.Data.isBooked $false
$replay = Invoke-Api 'POST' '/api/reservations/scan-complete' @{
    qrToken = $qr
    stationId = $mainStation.id
} $operatorToken
Check-Status 'QR replay rejected' $replay 400

$dashboardA = Invoke-Api 'GET' '/api/reports/my-dashboard' $null $tokenA
Check-Status 'Prosumer A dashboard' $dashboardA 200
Check-Value 'A PendingCount' $dashboardA.Data.pendingCount 1
Check-Value 'A ApprovedFutureCount' $dashboardA.Data.approvedFutureCount 1
Check-Value 'A CompletedCount' $dashboardA.Data.completedCount 1
Check-Value 'A CancelledCount' $dashboardA.Data.cancelledCount 1
Check-Value 'A NextReservation' $dashboardA.Data.nextReservation.id $approvedBooking.id

$dashboardB = Invoke-Api 'GET' '/api/reports/my-dashboard' $null $tokenB
Check-Status 'Prosumer B dashboard' $dashboardB 200
Check-Value 'B PendingCount' $dashboardB.Data.pendingCount 1
Check-Value 'B ApprovedFutureCount' $dashboardB.Data.approvedFutureCount 0
Check-Value 'B CompletedCount' $dashboardB.Data.completedCount 0
Check-Value 'B CancelledCount' $dashboardB.Data.cancelledCount 0

$operatorDashboard = Invoke-Api 'GET' "/api/reports/operator-dashboard?stationId=$($mainStation.id)" $null $operatorToken
Check-Status 'operator dashboard' $operatorDashboard 200
$today = [DateTime]::UtcNow.Date
$pendingToday = @(@($pendingBooking, $bBooking) | Where-Object { ([DateTime]$_.slotStartTime).ToUniversalTime().Date -eq $today }).Count
$approvedToday = @(@($approvedBooking) | Where-Object { ([DateTime]$_.slotStartTime).ToUniversalTime().Date -eq $today }).Count
Check-Value 'operator PendingToday' $operatorDashboard.Data.pendingToday $pendingToday
Check-Value 'operator ApprovedToday' $operatorDashboard.Data.approvedToday $approvedToday
Check-Value 'operator CompletedToday' $operatorDashboard.Data.completedToday 1
Check-Value 'operator ApprovedFutureCount' $operatorDashboard.Data.approvedFutureCount 1
Check-Value 'operator UpcomingApproved' $operatorDashboard.Data.upcomingApproved[0].id $approvedBooking.id

$allStations = Invoke-Api 'GET' '/api/reports/operator-dashboard' $null $adminToken
Check-Status 'all-stations operator dashboard' $allStations 200
$missingStation = Invoke-Api 'GET' '/api/reports/operator-dashboard?stationId=000000000000000000000000' $null $operatorToken
Check-Status 'unknown station rejected' $missingStation 400

foreach ($path in @(
    '/api/reports/dashboard-summary',
    '/api/reports/reservations-by-status',
    '/api/reports/reservations-per-day',
    '/api/reports/top-stations',
    '/api/reports/energy-traded',
    '/api/reports/recent-bookings',
    '/api/reports/pending-approvals'
)) {
    $forbidden = Invoke-Api 'GET' $path $null $tokenA
    Check-Status "Prosumer forbidden from $path" $forbidden 403
    $backoffice = Invoke-Api 'GET' $path $null $adminToken
    Check-Status "Backoffice can read $path" $backoffice 200
}

$concurrentSlot = New-Slot 12
$concurrentBooking = New-OwnReservation $concurrentSlot $tokenA
$concurrentApproval = Invoke-Api 'PUT' "/api/reservations/$($concurrentBooking.id)/approve" $null $adminToken
Check-Status 'approve concurrent-scan booking' $concurrentApproval 200
$concurrentQrResponse = Invoke-Api 'GET' "/api/reservations/my/$($concurrentBooking.id)/qr" $null $tokenA
Check-Status 'concurrent-scan QR' $concurrentQrResponse 200

Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $operatorToken)
$scanBody = @{ qrToken = $concurrentQrResponse.Data.qrToken; stationId = $mainStation.id } | ConvertTo-Json -Compress
$content1 = New-Object System.Net.Http.StringContent($scanBody, [Text.Encoding]::UTF8, 'application/json')
$content2 = New-Object System.Net.Http.StringContent($scanBody, [Text.Encoding]::UTF8, 'application/json')
try {
    $task1 = $client.PostAsync("$BaseUrl/api/reservations/scan-complete", $content1)
    $task2 = $client.PostAsync("$BaseUrl/api/reservations/scan-complete", $content2)
    [System.Threading.Tasks.Task]::WaitAll(@($task1, $task2))
    $concurrentCodes = @([int]$task1.Result.StatusCode, [int]$task2.Result.StatusCode) | Sort-Object
    Check-Value 'concurrent scan HTTP codes' ($concurrentCodes -join ',') '200,400'
}
finally {
    $content1.Dispose()
    $content2.Dispose()
    $client.Dispose()
}
$concurrentSlotAfter = Invoke-Api 'GET' "/api/slots/$($concurrentSlot.id)" $null $adminToken
Check-Status 'concurrent-scan slot lookup' $concurrentSlotAfter 200
Check-Value 'concurrent-scan slot freed' $concurrentSlotAfter.Data.isBooked $false

Write-Output "Stage 5 live smoke test passed. Test NICs: $nicA, $nicB"
