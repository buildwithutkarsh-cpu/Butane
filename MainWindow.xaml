<Window x:Class="Butane.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Butane" Icon="pack://application:,,,/butane.ico"
        Height="760" Width="1200" MinWidth="720" MinHeight="480"
        Background="{StaticResource BgBrush}" PreviewKeyDown="Window_PreviewKeyDown">

    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition x:Name="SidebarColumn" Width="260" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>

        <!-- ===== Sidebar ===== -->
        <Border x:Name="SidebarPanel" Grid.Column="0" Background="{StaticResource BgBrush}"
                BorderBrush="{StaticResource LineBrush}" BorderThickness="0,0,1,0">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="60" />
                    <RowDefinition Height="46" />
                    <RowDefinition Height="*" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>

                <!-- Wordmark -->
                <Border Grid.Row="0" BorderBrush="{StaticResource LineBrush}" BorderThickness="0,0,0,1">
                    <Grid>
                        <StackPanel Margin="20,0,0,0" VerticalAlignment="Center">
                            <TextBlock Text="BUTANE" Foreground="White" FontFamily="{StaticResource Mono}"
                                       FontSize="22" FontWeight="Black" />
                            <TextBlock x:Name="PrivateModeLabel" Text="PRIVATE" Foreground="{DynamicResource AccentBrush}"
                                       FontFamily="{StaticResource Mono}" FontSize="10" FontWeight="Bold"
                                       Visibility="Collapsed" />
                        </StackPanel>
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                            <Button Content="WINDOW" Style="{StaticResource FlatButton}" Foreground="{StaticResource DimBrush}"
                                    Width="68" Height="59" ToolTip="New window (Ctrl+N)" Click="NewWindow_Click" />
                            <Button Content="PRIVATE" Style="{StaticResource FlatButton}" Foreground="{StaticResource DimBrush}"
                                    Width="68" Height="59" ToolTip="Private window (Ctrl+Shift+N)"
                                    Click="PrivateWindow_Click" />
                        </StackPanel>
                    </Grid>
                </Border>

                <!-- Tabs header -->
                <Grid Grid.Row="1">
                    <TextBlock Text="TABS" Foreground="White" FontFamily="{StaticResource Mono}"
                               FontSize="12" FontWeight="Bold" VerticalAlignment="Center" Margin="20,0,0,0" />
                    <Button Content="+ NEW" Style="{StaticResource FlatButton}" Foreground="{StaticResource DimBrush}"
                            HorizontalAlignment="Right" Width="70" Height="46" ToolTip="New tab (Ctrl+T)"
                            Click="NewTab_Click" />
                </Grid>

                <!-- Tab list -->
                <ListBox x:Name="TabList" Grid.Row="2" Background="Transparent" BorderThickness="0"
                         AllowDrop="True" Drop="TabList_Drop"
                         PreviewMouseLeftButtonDown="TabList_PreviewMouseLeftButtonDown"
                         PreviewMouseMove="TabList_PreviewMouseMove"
                         ItemContainerStyle="{StaticResource TabItemStyle}"
                         ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                         SelectionChanged="TabList_SelectionChanged">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Grid Background="Transparent">
                                <Grid.ContextMenu>
                                    <ContextMenu>
                                        <MenuItem Header="Pin / unpin tab" Click="TabMenu_Pin_Click" />
                                        <MenuItem Header="Mute / unmute tab" Click="TabMenu_Mute_Click" />
                                        <MenuItem Header="Reload" Click="TabMenu_Reload_Click" />
                                        <MenuItem Header="Duplicate" Click="TabMenu_Duplicate_Click" />
                                        <Separator />
                                        <MenuItem Header="Close other tabs" Click="TabMenu_CloseOthers_Click" />
                                        <MenuItem Header="Close tabs below" Click="TabMenu_CloseBelow_Click" />
                                        <MenuItem Header="Close tab" Click="TabMenu_Close_Click" />
                                    </ContextMenu>
                                </Grid.ContextMenu>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <Image Source="{Binding Favicon}" Width="14" Height="14" Margin="0,0,8,0"
                                       VerticalAlignment="Center" />
                                <TextBlock Grid.Column="1" Text="{Binding Display}" TextTrimming="CharacterEllipsis"
                                           VerticalAlignment="Center" />
                                <Button Grid.Column="2" Content="&#xD7;" FontSize="16" Width="28" Height="28"
                                        Style="{StaticResource FlatButton}" Foreground="{StaticResource DimBrush}"
                                        ToolTip="Close tab (Ctrl+W)" Click="CloseTab_Click" />
                            </Grid>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>

                <!-- Shield section -->
                <StackPanel Grid.Row="3">
                    <Border BorderBrush="{StaticResource LineBrush}" BorderThickness="0,1,0,0" Height="40">
                        <TextBlock Text="TRACKING PROTECTION" Foreground="White" FontFamily="{StaticResource Mono}"
                                   FontSize="11" FontWeight="Bold" VerticalAlignment="Center" Margin="20,0,0,0" />
                    </Border>
                    <ToggleButton x:Name="ShieldToggle" Style="{StaticResource FlatToggle}" Height="40"
                                  IsChecked="True" Click="ShieldToggle_Click">
                        <Grid Margin="20,0">
                            <TextBlock Text="BUTANE SHIELD" />
                            <TextBlock x:Name="ShieldState" Text="ON" HorizontalAlignment="Right"
                                       Foreground="{StaticResource DimBrush}" />
                        </Grid>
                    </ToggleButton>
                    <Border BorderBrush="{StaticResource LineBrush}" BorderThickness="0,1,0,0" Height="40">
                        <Grid Margin="20,0">
                            <TextBlock Text="WEBVIEW PROTECTION" Foreground="White" FontFamily="{StaticResource Mono}"
                                       FontSize="10" FontWeight="Bold" VerticalAlignment="Center" />
                            <TextBlock Text="BALANCED" Foreground="{StaticResource DimBrush}" FontFamily="{StaticResource Mono}"
                                       FontSize="10" FontWeight="Bold" HorizontalAlignment="Right" VerticalAlignment="Center" />
                        </Grid>
                    </Border>
                    <Button x:Name="SiteExceptionButton" Content="ALLOW CURRENT SITE"
                            Style="{StaticResource FlatButton}" Height="38" IsEnabled="False"
                            ToolTip="Bypass Butane's tracker blocklist for this site. WebView tracking prevention still applies."
                            Click="SiteException_Click" />
                    <Button Content="SETTINGS" Style="{StaticResource FlatButton}" Height="38"
                            Click="SettingsButton_Click" />
                </StackPanel>
            </Grid>
        </Border>

        <!-- ===== Main area ===== -->
        <Grid Grid.Column="1">
            <Grid.RowDefinitions>
                <RowDefinition x:Name="TopRow" Height="96" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
                <RowDefinition x:Name="BottomRow" Height="40" />
            </Grid.RowDefinitions>

            <!-- Address and library controls -->
            <Grid x:Name="TopPanel" Grid.Row="0">
                <Grid.RowDefinitions>
                    <RowDefinition Height="60" />
                    <RowDefinition Height="36" />
                </Grid.RowDefinitions>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="300" />
                </Grid.ColumnDefinitions>

                <Border Grid.Row="0" Grid.Column="0" BorderBrush="{StaticResource LineBrush}" BorderThickness="0,0,0,1">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="56" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="44" />
                            <ColumnDefinition Width="64" />
                        </Grid.ColumnDefinitions>

                        <TextBlock x:Name="SecurityIcon" Text="&#xE721;" FontFamily="Segoe MDL2 Assets" FontSize="16"
                                   Foreground="White" HorizontalAlignment="Center" VerticalAlignment="Center" />

                        <TextBox x:Name="AddressBar" Grid.Column="1" Background="Transparent" BorderThickness="0"
                                 Foreground="White" CaretBrush="White" SelectionBrush="#555555"
                                 FontFamily="{StaticResource Mono}" FontSize="17"
                                 VerticalContentAlignment="Center"
                                 KeyDown="AddressBar_KeyDown" GotKeyboardFocus="AddressBar_GotKeyboardFocus"
                                 LostKeyboardFocus="AddressBar_LostKeyboardFocus"
                                 TextChanged="AddressBar_TextChanged" />

                        <Popup x:Name="SuggestPopup" Grid.Column="1" PlacementTarget="{Binding ElementName=AddressBar}"
                               Placement="Bottom" StaysOpen="True" Focusable="False">
                            <Border x:Name="SuggestBorder" Background="#1B1B1B" BorderBrush="{StaticResource LineBrush}"
                                    BorderThickness="1">
                                <ListBox x:Name="SuggestList" Background="Transparent" BorderThickness="0"
                                         Foreground="White" FontFamily="{StaticResource Mono}" FontSize="13"
                                         MaxHeight="320" Focusable="False"
                                         ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                                         PreviewMouseLeftButtonUp="SuggestList_MouseUp" />
                            </Border>
                        </Popup>

                        <!-- Placeholder, visible only while the box is empty -->
                        <TextBlock Grid.Column="1" Text="SEARCH OR ENTER ADDRESS" IsHitTestVisible="False"
                                   Foreground="{StaticResource DimBrush}" FontFamily="{StaticResource Mono}"
                                   FontSize="17" VerticalAlignment="Center" Margin="3,0,0,0">
                            <TextBlock.Style>
                                <Style TargetType="TextBlock">
                                    <Setter Property="Visibility" Value="Collapsed" />
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding Text, ElementName=AddressBar}" Value="">
                                            <Setter Property="Visibility" Value="Visible" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </TextBlock.Style>
                        </TextBlock>

                        <Button x:Name="SaveBookmarkButton" Grid.Column="2" Content="SAVE"
                                Style="{StaticResource FlatButton}" ToolTip="Bookmark this page (Ctrl+D)"
                                Click="BookmarkButton_Click" />

                        <Button x:Name="MenuButton" Grid.Column="3" Content="MENU"
                                Style="{StaticResource FlatButton}" ToolTip="Browser menu"
                                Click="MenuButton_Click" />
                    </Grid>
                </Border>

                <Border Grid.Row="0" Grid.Column="1" Background="White">
                    <Grid Margin="12,0">
                        <TextBlock Text="BLOCKED" Foreground="Black" FontFamily="{StaticResource Mono}"
                                   FontSize="13" FontWeight="Bold" VerticalAlignment="Center" />
                        <TextBlock x:Name="BlockedCount" Text="0" Foreground="Black" FontFamily="{StaticResource Mono}"
                                   FontSize="13" FontWeight="Bold" HorizontalAlignment="Right" VerticalAlignment="Center" />
                    </Grid>
                </Border>

                <UniformGrid Grid.Row="1" Grid.Column="1" Columns="3">
                    <Button x:Name="BookmarksManagerButton" Content="BOOKMARKS"
                            Style="{StaticResource FlatButton}" FontSize="10" ToolTip="Open bookmarks"
                            Click="BookmarksButton_Click" />
                    <Button x:Name="HistoryManagerButton" Content="HISTORY" FontSize="10" Style="{StaticResource FlatButton}"
                            ToolTip="Browsing history" Click="HistoryButton_Click" />
                    <Button Content="DOWNLOADS" FontSize="10" Style="{StaticResource FlatButton}"
                            ToolTip="Downloads manager" Click="DownloadsButton_Click" />
                </UniformGrid>
                <!-- Keep the counter and library actions in separate rows to avoid clipping on narrow windows. -->
                <Border Grid.Row="1" Grid.Column="0" BorderBrush="{StaticResource LineBrush}" BorderThickness="0,0,0,1" />
            </Grid>

            <!-- Bookmarks bar -->
            <Border x:Name="BookmarksBarPanel" Grid.Row="1" Height="32" Visibility="Collapsed"
                    BorderBrush="{StaticResource LineBrush}" BorderThickness="0,0,0,1">
                <ScrollViewer HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Disabled">
                    <ItemsControl x:Name="BookmarksBar">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate>
                                <StackPanel Orientation="Horizontal" />
                            </ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Button Style="{StaticResource FlatButton}" FontSize="11" MaxWidth="200"
                                        Padding="14,0" ToolTip="{Binding Url}" Click="BookmarkBarItem_Click">
                                    <Button.ContextMenu>
                                        <ContextMenu>
                                            <MenuItem Header="Open in new tab" Click="BookmarkBarOpenNewTab_Click" />
                                            <MenuItem Header="Remove bookmark" Click="BookmarkBarRemove_Click" />
                                        </ContextMenu>
                                    </Button.ContextMenu>
                                    <TextBlock Text="{Binding Title}" TextTrimming="CharacterEllipsis" Margin="14,0" />
                                </Button>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </Border>

            <!-- Find in page (its own row so it is never hidden behind the web view) -->
            <Border x:Name="FindBar" Grid.Row="2" Height="38" Visibility="Collapsed"
                    BorderBrush="{StaticResource LineBrush}" BorderThickness="0,0,0,1">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBox x:Name="FindBox" Background="Transparent" BorderThickness="0" Foreground="White"
                             CaretBrush="White" FontFamily="{StaticResource Mono}" FontSize="14"
                             VerticalContentAlignment="Center" Padding="16,0"
                             TextChanged="FindBox_TextChanged" KeyDown="FindBox_KeyDown" />
                    <TextBlock x:Name="FindResult" Grid.Column="1" Foreground="{StaticResource DimBrush}"
                               FontFamily="{StaticResource Mono}" FontSize="11" FontWeight="Bold"
                               VerticalAlignment="Center" Margin="0,0,14,0" />
                    <CheckBox x:Name="FindCase" Grid.Column="2" Content="MATCH CASE" Foreground="White"
                              FontFamily="{StaticResource Mono}" FontSize="11" VerticalAlignment="Center"
                              Margin="0,0,10,0" Checked="FindBox_TextChanged" Unchecked="FindBox_TextChanged" />
                    <Button Grid.Column="3" Content="PREV" Width="70" Style="{StaticResource FlatButton}"
                            Click="FindPrev_Click" />
                    <Button Grid.Column="4" Content="NEXT" Width="70" Style="{StaticResource FlatButton}"
                            Click="FindNext_Click" />
                    <Button Grid.Column="5" Content="CLOSE" Width="80" Style="{StaticResource FlatButton}"
                            Click="FindClose_Click" />
                </Grid>
            </Border>

            <!-- Web pages live here (one WebView2 per tab, added in code) -->
            <Grid x:Name="WebHost" Grid.Row="3" Background="{StaticResource BgBrush}" />

            <!-- Bottom bar: status + navigation -->
            <Border x:Name="BottomPanel" Grid.Row="4" BorderBrush="{StaticResource LineBrush}" BorderThickness="0,1,0,0">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>

                    <TextBlock x:Name="StatusText" Text="READY" Foreground="White"
                               FontFamily="{StaticResource Mono}" FontSize="12" FontWeight="Bold"
                               VerticalAlignment="Center" Margin="22,0,0,0" />

                    <StackPanel Grid.Column="1" Orientation="Horizontal">
                        <Border Width="1" Background="{StaticResource LineBrush}" />
                        <Button x:Name="BackButton" Content="BACK" Width="90" IsEnabled="False"
                                Style="{StaticResource FlatButton}" Click="BackButton_Click" />
                        <Border Width="1" Background="{StaticResource LineBrush}" />
                        <Button x:Name="ForwardButton" Content="FORWARD" Width="100" IsEnabled="False"
                                Style="{StaticResource FlatButton}" Click="ForwardButton_Click" />
                        <Border Width="1" Background="{StaticResource LineBrush}" />
                        <Button x:Name="ReloadButton" Content="RELOAD" Width="100"
                                Style="{StaticResource FlatButton}" Click="ReloadButton_Click" />
                    </StackPanel>
                </Grid>
            </Border>
        </Grid>
    </Grid>
</Window>
